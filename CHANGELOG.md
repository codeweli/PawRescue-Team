# Changelog

## Member 3 finished: map filters + clustering (Nov 13, 2026)
- Map **Urgency** (All / Medium+ / High+ / Critical) and **Status** (Open / Reported / In Progress / Rescued / All) pickers now filter the dots (`MapViewModel.MinSeverity()` / `StatusFilter()`).
- **Grid-based marker clustering** (`MarkerClusterer.Cluster`): nearby reports merge into one "N reports here" dot when zoomed out (colored by the most urgent report); tapping it zooms in.

## Status update sheet + Member 4 finished (Nov 13, 2026)
- "Start rescue / Mark as rescued / Close case" now opens a styled bottom sheet with quick-note chips ("On my way", "Brought to the vet", …) and a note box, instead of the plain Android prompt.
- Member 4's remaining TODOs completed: Admin **Activity** tab (`AuditLogRepository.GetRecentAsync`) and tapping a user to change role / activate / deactivate (`ManageUserAsync`).

## Friendlier look, new Animal Details, photo-only samples (Nov 13, 2026)
- **Font:** the whole app now uses **Nunito**, a soft rounded font (free, SIL Open Font License, `Resources/Fonts/`). Headings keep the serif style.
- **Animal Details redesigned:** big photo, rounded white card, soft pills (need · urgency · status), quick-facts tiles, "About this animal", a location card with **View on map** + **Directions**, rescuer card, and a checkmark progress timeline.
- **Accept Rescue** now opens a friendly bottom sheet ("Help Luna?" / "Yes, I'll help" / "Not now") instead of the plain Android dialog.
- **Removed** the "AI health check (beta)" button (the API endpoint stays as a documented future feature).
- **Sample data:** 8 animals, **all with photos** (4 cats + 4 dogs) in San Pablo City. A sample is only created if its photo exists. Old or picture-less demo reports are removed automatically on API start.

## Status bar overlap, "show on map", safer directions (Nov 13, 2026)
- **Fixed:** Home (and the other tab pages) no longer draw under the phone's status bar on Android 15+ (edge-to-edge). See `Views/BasePage.cs`.
- **Animal Details → tap the Location card** opens the in-app Rescue Map zoomed on that exact report with its preview card.
- **Directions** when your location is unknown/outside the Philippines now shows the exact pin in Google Maps instead of routing from the phone's own (possibly wrong) location.

## Sample data moved to San Pablo City, Laguna (Nov 13, 2026)
- All 9 demo reports are now in **San Pablo City, Laguna** (cathedral, old city hall, Sampaloc Lake, public market, Brgy. San Marcos).
- **4 sample cats with real photos** (`wwwroot/samples/`): Kuting, Luna, Bella, Mingming. They're the newest, so they show first.
- The map starts on San Pablo City when GPS is off or outside the Philippines.
- Old demo reports from the earlier Philippines-wide sample set are **removed automatically** on the next API start (your own reports are never touched).

## Directions, your location, route on the map, sample photos (Nov 13, 2026)
- **Fixed misleading directions:** "Directions" used to pass the animal's *name* to Google Maps, which searched it as a place (e.g. "Luna" is also a town in La Union). It now opens Google Maps with the **exact coordinates**, starting from **your current GPS position**.
- **Blue "you are here" dot** on the Rescue Map.
- **Route inside the app:** on the map preview card, **Directions** draws the road route from you to the animal (free OSRM routing, no key) and shows the distance and drive time. **Navigate** then opens turn-by-turn directions in Google Maps.
- **Sample photos:** put `luna.jpg`, `brownie.jpg`, `max.jpg`, `bella.jpg` in `src/PawRescue.Api/wwwroot/samples/` and restart the API; they're attached to those sample reports automatically. These 4 samples are now the newest, so they show first.
- New package: `Mapsui.Nts` (draws lines on the map).

## Map fixes, place search, Philippine sample data (Nov 13, 2026)
- **Map stays on the Philippines:** can't pan outside the country or zoom out past it. GPS outside the Philippines (like the emulator's default California location) is ignored, and the map starts in Manila.
- **Search bar finds places:** typing a place ("Muñoz", "SM North EDSA", "Cebu City") moves the map there, using OpenStreetMap's free Nominatim search limited to the Philippines. If no place matches, it searches reports instead.
- **Less buggy map:** pins redraw once per reload instead of once per pin (stops flicker), and the preview card no longer closes by itself after the map reloads.
- **Addresses** for new reports now come from OpenStreetMap (works on the emulator), with the phone's geocoder as a fallback.
- **Sample data:** an empty database is filled with 12 demo reports in real Philippine places (Muñoz, Cabanatuan, Palayan, Tarlac, Quezon City, Manila, Makati, Baguio, Cebu, Iloilo, Davao), plus demo accounts `reporter@pawrescue.local` / `volunteer@pawrescue.local` (password `Demo@12345`). Turn off with `"Seed": { "SampleData": false }`.

## Map switched to OpenStreetMap (Nov 13, 2026)
- Replaced Google Maps (`Microsoft.Maui.Controls.Maps`) with **OpenStreetMap through Mapsui** (`Mapsui.Maui`). It's free and needs **no API key and no credit card**.
- `MauiProgram.cs`: `.UseMauiMaps()` → `.UseSkiaSharp()`; removed the Google pin-color code and `Controls/ColoredPin.cs`.
- `AndroidManifest.xml`: removed the Google Maps API key line.
- `Views/MapPage.xaml(.cs)`: new map control, colored report dots, tap a dot → preview card, "© OpenStreetMap contributors" credit.
- `MapViewModel` is unchanged, so Member 3's filter/clustering TODOs work exactly the same.

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
- **Rescue Map:** rounded map, colored dots (red medical, orange food, yellow shelter, green helped), legend, and a bottom preview card with "View details".
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
