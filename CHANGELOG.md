# Changelog

## UI redesign + fixes (Nov 13, 2026)

### Fixed
- **App would not build:** `AppShell.xaml.cs` had been overwritten with the `App` class (two `CreateWindow` methods and no `AppShell` constructor). Restored `AppShell.xaml.cs`, and moved the crash fix into `App.xaml.cs` where it belongs (AppShell is created after the styles load).
- **Wrong .NET SDK picked on some laptops:** added `global.json` so everyone builds with the .NET 10 SDK.
- **.NET 10 warnings:** replaced obsolete `DisplayAlert`, `DisplayActionSheet`, `ScaleTo` and `PickPhotoAsync` with their new `...Async` / `PickPhotosAsync` versions.
- After submitting a report, Back now returns to where you started instead of the empty form.

### New design (matches the mockups)
- Crimson/pink theme, serif headings, white background (`Resources/Styles/Colors.xaml`, `Styles.xaml`).
- New outline icons (`Resources/Images/icon_*.svg`, `tab_*.svg`).
- Bottom tabs are now **Home · Map · Reports · Alerts · Profile**. Reporting an animal opens from the Home tile or the red **+** on Reports.
- **Home:** logo + greeting header, bell with unread dot, search bar, 3 action tiles, community stats card, "Needs help nearby" cards.
- **Report cards:** photo, animal name, type, location, colored need dot ("Medical Treatment", "Food and Water", …) and a "! High Priority" / "• Open" badge.
- **Rescue Map:** rounded map, colored pins (red medical, orange food, yellow shelter, green helped), legend, and a bottom preview card with "View details".
- **Rescue Cases (new tab):** filter chips (All, High Priority, Open, Dogs, Cats, Birds, Rescued, My reports) + search.
- **Notifications:** tinted round icons, "12 min ago" times, unread dots, dividers.
- **Profile:** pink header card, Reports / Assists / Rescued stats, menu list.

### New features
- `GET /api/reports/stats`: community numbers for the Home screen.
- **Need categories** (medical / food / shelter / helped), worked out from condition + status. No database change.
- **Share a report** (Share button on Animal Details) with a Google Maps link.
- **Help and safety tips** page.
- **Notification settings** page: turn nearby alerts on/off and choose the Home search distance.
- Home search jumps to Rescue Cases with the keyword.

### Still assigned to team members (TODOs left in the code)
- **Member 3:** `MarkerClusterer.Cluster` (pin grouping) and `MinSeverity()` / `StatusFilter()` in `MapViewModel`. The map pickers do nothing until these are done.
- **Member 4:** `AuditLogRepository.GetRecentAsync` (the Admin "Activity" tab errors until it's written) and `ManageUserAsync` in `AdminDashboardViewModel` (tapping a user).
