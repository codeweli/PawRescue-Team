using Microsoft.Extensions.Logging.Abstractions;
using PawRescue.Application.Common;
using PawRescue.Application.DTOs;
using PawRescue.Application.Interfaces;
using PawRescue.Application.Services;
using PawRescue.Domain.Entities;
using PawRescue.Domain.Enums;
using Xunit;

namespace PawRescue.Tests;

public class NotificationDomainTests
{
    [Fact]
    public void New_notification_is_unread_by_default()
    {
        var userId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var n = new Notification(userId, reportId, NotificationType.RescueAccepted, "Rescue accepted", "A volunteer is on the way");

        Assert.Equal(userId, n.UserId);
        Assert.Equal(reportId, n.ReportId);
        Assert.Equal(NotificationType.RescueAccepted, n.Type);
        Assert.Equal("Rescue accepted", n.Title);
        Assert.Equal("A volunteer is on the way", n.Message);
        Assert.False(n.IsRead);
        Assert.Null(n.ReadAt);
    }

    [Fact]
    public void MarkAsRead_sets_IsRead_and_ReadAt()
    {
        var n = new Notification(Guid.NewGuid(), null, NotificationType.StatusChanged, "Status changed", "Report is closed");
        var before = DateTime.UtcNow;

        n.MarkAsRead();

        var after = DateTime.UtcNow;
        Assert.True(n.IsRead);
        Assert.NotNull(n.ReadAt);
        Assert.InRange(n.ReadAt!.Value, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public void MarkAsRead_when_already_read_does_not_change_ReadAt()
    {
        var n = new Notification(Guid.NewGuid(), null, NotificationType.StatusChanged, "Status", "Msg");
        n.MarkAsRead();
        var initialReadAt = n.ReadAt;

        n.MarkAsRead();

        Assert.True(n.IsRead);
        Assert.Equal(initialReadAt, n.ReadAt);
    }
}

public class NotificationServiceTests
{
    private class FakeCurrentUserService : ICurrentUserService
    {
        public Guid? UserId { get; set; } = Guid.NewGuid();
        public string? Role { get; set; } = "User";
        public string? IpAddress { get; set; } = "127.0.0.1";
        public bool IsAuthenticated => UserId.HasValue;
        public bool IsInRole(string role) => Role == role;
    }

    private class FakeUnitOfWork : IUnitOfWork
    {
        public int SavedCount { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SavedCount++;
            return Task.FromResult(1);
        }
    }

    private class FakePushSender : IPushNotificationSender
    {
        public Task SendAsync(string deviceToken, string title, string body, IDictionary<string, string>? data = null, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private class FakeUserRepository : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task AddAsync(User entity, CancellationToken ct = default) => Task.CompletedTask;
        public void Remove(User entity) { }
        public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<User?> GetWithRoleAsync(Guid id, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyList<User>> GetVolunteersInBoxAsync(double minLat, double maxLat, double minLon, double maxLon, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>());
        public Task<(IReadOnlyList<User> Items, int Total)> SearchAsync(string? keyword, int page, int pageSize, CancellationToken ct = default)
            => Task.FromResult<(IReadOnlyList<User>, int)>((Array.Empty<User>(), 0));
        public Task<int> CountAsync(int? roleId = null, CancellationToken ct = default) => Task.FromResult(0);
    }

    private class FakeNotificationRepository : INotificationRepository
    {
        public readonly List<Notification> Notifications = new();
        public int LastPageRequested { get; private set; }
        public int LastPageSizeRequested { get; private set; }
        public bool MarkAllAsReadCalled { get; private set; }

        public Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Notifications.FirstOrDefault(n => n.Id == id));

        public Task AddAsync(Notification entity, CancellationToken ct = default)
        {
            Notifications.Add(entity);
            return Task.CompletedTask;
        }

        public void Remove(Notification entity) => Notifications.Remove(entity);

        public Task<(IReadOnlyList<Notification> Items, int Total)> GetForUserAsync(Guid userId, bool unreadOnly, int page, int pageSize, CancellationToken ct = default)
        {
            LastPageRequested = page;
            LastPageSizeRequested = pageSize;

            var q = Notifications.Where(n => n.UserId == userId);
            if (unreadOnly) q = q.Where(n => !n.IsRead);
            var list = q.ToList();
            var paged = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult<(IReadOnlyList<Notification>, int)>((paged, list.Count));
        }

        public Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(Notifications.Count(n => n.UserId == userId && !n.IsRead));

        public Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default)
        {
            MarkAllAsReadCalled = true;
            foreach (var n in Notifications.Where(n => n.UserId == userId && !n.IsRead))
                n.MarkAsRead();
            return Task.CompletedTask;
        }
    }

    private readonly FakeNotificationRepository _notificationRepo = new();
    private readonly FakeUserRepository _userRepo = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakePushSender _push = new();
    private readonly FakeCurrentUserService _currentUser = new();

    private NotificationService CreateService()
        => new(_notificationRepo, _userRepo, _uow, _push, _currentUser, NullLogger<NotificationService>.Instance);

    [Fact]
    public async Task GetMyNotificationsAsync_clamps_page_and_pageSize()
    {
        var service = CreateService();

        await service.GetMyNotificationsAsync(unreadOnly: false, page: -5, pageSize: 500);

        Assert.Equal(1, _notificationRepo.LastPageRequested);
        Assert.Equal(100, _notificationRepo.LastPageSizeRequested);
    }

    [Fact]
    public async Task GetMyNotificationsAsync_throws_Unauthorized_when_not_logged_in()
    {
        _currentUser.UserId = null;
        var service = CreateService();

        await Assert.ThrowsAsync<UnauthorizedException>(() => service.GetMyNotificationsAsync(false, 1, 20));
    }

    [Fact]
    public async Task GetUnreadCountAsync_returns_unread_count_for_current_user()
    {
        var myId = _currentUser.UserId!.Value;
        var otherId = Guid.NewGuid();

        _notificationRepo.Notifications.Add(new Notification(myId, null, NotificationType.RescueAccepted, "T1", "M1"));
        _notificationRepo.Notifications.Add(new Notification(myId, null, NotificationType.StatusChanged, "T2", "M2"));
        var readNotif = new Notification(myId, null, NotificationType.NewNearbyReport, "T3", "M3");
        readNotif.MarkAsRead();
        _notificationRepo.Notifications.Add(readNotif);
        _notificationRepo.Notifications.Add(new Notification(otherId, null, NotificationType.RescueAccepted, "T4", "M4"));

        var service = CreateService();
        var count = await service.GetUnreadCountAsync();

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task MarkAsReadAsync_marks_notification_as_read_and_saves()
    {
        var myId = _currentUser.UserId!.Value;
        var notif = new Notification(myId, null, NotificationType.RescueAccepted, "Title", "Msg");
        _notificationRepo.Notifications.Add(notif);

        var service = CreateService();
        await service.MarkAsReadAsync(notif.Id);

        Assert.True(notif.IsRead);
        Assert.NotNull(notif.ReadAt);
        Assert.Equal(1, _uow.SavedCount);
    }

    [Fact]
    public async Task MarkAsReadAsync_when_notification_belongs_to_another_user_throws_ForbiddenException()
    {
        var otherId = Guid.NewGuid();
        var notif = new Notification(otherId, null, NotificationType.RescueAccepted, "Title", "Msg");
        _notificationRepo.Notifications.Add(notif);

        var service = CreateService();
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => service.MarkAsReadAsync(notif.Id));

        Assert.Equal(403, ex.StatusCode);
        Assert.False(notif.IsRead);
        Assert.Equal(0, _uow.SavedCount);
    }

    [Fact]
    public async Task MarkAsReadAsync_when_notification_not_found_throws_NotFoundException()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => service.MarkAsReadAsync(Guid.NewGuid()));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal(0, _uow.SavedCount);
    }

    [Fact]
    public async Task MarkAllAsReadAsync_marks_all_as_read_and_saves()
    {
        var myId = _currentUser.UserId!.Value;
        var notif1 = new Notification(myId, null, NotificationType.RescueAccepted, "T1", "M1");
        var notif2 = new Notification(myId, null, NotificationType.StatusChanged, "T2", "M2");
        _notificationRepo.Notifications.Add(notif1);
        _notificationRepo.Notifications.Add(notif2);

        var service = CreateService();
        await service.MarkAllAsReadAsync();

        Assert.True(_notificationRepo.MarkAllAsReadCalled);
        Assert.True(notif1.IsRead);
        Assert.True(notif2.IsRead);
        Assert.Equal(1, _uow.SavedCount);
    }
}
