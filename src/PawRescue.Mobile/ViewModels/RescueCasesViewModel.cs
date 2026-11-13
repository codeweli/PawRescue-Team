using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PawRescue.Mobile.Models;
using PawRescue.Mobile.Services;

namespace PawRescue.Mobile.ViewModels;

/// <summary>
/// "Rescue Cases" tab: every report with quick filter chips
/// (All · High Priority · Open · Dogs · Cats · Birds · Rescued · My reports).
/// The list is loaded once and the chips filter it on the phone, so switching chips is instant.
/// </summary>
public partial class RescueCasesViewModel : BaseViewModel
{
    public const string All = "All";
    public const string HighPriority = "High Priority";
    public const string Open = "Open";
    public const string Dogs = "Dogs";
    public const string Cats = "Cats";
    public const string Birds = "Birds";
    public const string Rescued = "Rescued";
    public const string Mine = "My reports";

    private readonly IReportApi _reports;
    private List<ReportSummaryDto> _all = new();
    private List<ReportSummaryDto>? _mine;

    public RescueCasesViewModel(IReportApi reports, IDialogService dialogs) : base(dialogs)
    {
        _reports = reports;
        Title = "Rescue Cases";
    }

    public string[] Filters { get; } = { All, HighPriority, Open, Dogs, Cats, Birds, Rescued, Mine };

    public ObservableCollection<ReportSummaryDto> Cases { get; } = new();

    [ObservableProperty] private string selectedFilter = All;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string countText = string.Empty;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private ReportSummaryDto? selectedCase;

    public override Task OnAppearingAsync() => LoadAsync();

    /// <summary>Called by the Home search bar before switching to this tab.</summary>
    public void PrepareSearch(string? text)
    {
        SearchText = text?.Trim() ?? string.Empty;
        SelectedFilter = All;
    }

    /// <summary>Called by Profile → "My reports".</summary>
    public void ShowMyReports()
    {
        SearchText = string.Empty;
        SelectedFilter = Mine;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var page = await _reports.SearchAsync(string.IsNullOrWhiteSpace(SearchText) ? null : SearchText);
            _all = page.Items;
            _mine = SelectedFilter == Mine ? await _reports.GetMineAsync() : null;
            ApplyFilter();
        }, showBusy: !IsRefreshing);
        IsRefreshing = false;
    }

    [RelayCommand]
    private Task SearchAsync() => LoadAsync();

    partial void OnSelectedFilterChanged(string value)
    {
        if (value == Mine && _mine is null)
            LoadCommand.Execute(null);   // "My reports" needs its own request
        else
            ApplyFilter();
    }

    private void ApplyFilter()
    {
        IEnumerable<ReportSummaryDto> source = SelectedFilter == Mine ? _mine ?? new() : _all;

        var filtered = SelectedFilter switch
        {
            HighPriority => source.Where(r => r.IsHighPriority),
            Open => source.Where(r => r.IsOpen),
            Dogs => source.Where(r => r.AnimalType == AnimalType.Dog),
            Cats => source.Where(r => r.AnimalType == AnimalType.Cat),
            Birds => source.Where(r => r.AnimalType == AnimalType.Bird),
            Rescued => source.Where(r => !r.IsOpen),
            _ => source
        };

        // Urgent first, then newest
        var list = filtered.OrderByDescending(r => r.IsHighPriority).ThenByDescending(r => r.CreatedAt).ToList();

        Cases.Clear();
        foreach (var r in list) Cases.Add(r);

        CountText = list.Count == 1 ? "1 report" : $"{list.Count} reports";
        if (!string.IsNullOrWhiteSpace(SearchText)) CountText += $" for \"{SearchText}\"";
    }

    [RelayCommand]
    private Task ClearSearchAsync()
    {
        SearchText = string.Empty;
        return LoadAsync();
    }

    partial void OnSelectedCaseChanged(ReportSummaryDto? value)
    {
        if (value is null) return;
        SelectedCase = null;
        _ = Shell.Current.GoToAsync("reportdetails", new Dictionary<string, object> { ["id"] = value.Id });
    }

    [RelayCommand]
    private Task NewReportAsync() => Shell.Current.GoToAsync("reportanimal");
}
