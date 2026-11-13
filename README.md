# 🐾 Paw Rescue

A mobile app for reporting stray, injured, sick, lost or abandoned animals. Users snap a photo, the app captures GPS, and the report is pinned on a map so volunteers can accept and track the rescue.

BS Computer Science Capstone — **.NET MAUI (MVVM) + ASP.NET Core Web API + PostgreSQL + EF Core + JWT**.

---

### Requirements
- .NET 10 SDK, with the MAUI workload: `dotnet workload install maui-android`
- PostgreSQL 15+ (or Docker)
- Visual Studio 2022/2026 or VS Code with the C# Dev Kit + .NET MAUI extension
- Android emulator (or a phone on the same Wi-Fi)

### Database
```bash
docker run --name pawrescue-db -e POSTGRES_PASSWORD=postgres -p 5432:5432 -d postgres:16
```
The API creates the tables automatically on first run. For real migrations (recommended before defense/deploy):
```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate -p src/PawRescue.Infrastructure -s src/PawRescue.Api
```
After that, the API applies migrations on start-up instead of `EnsureCreated`.

### API
```bash
cd src/PawRescue.Api
dotnet user-secrets set "Jwt:SigningKey" "a-long-random-secret-of-at-least-32-characters"
dotnet run
```
Open **http://localhost:5080/swagger**. A seeded admin is created: `admin@pawrescue.local` / `Admin@12345` (change it in `appsettings.json` → `Seed`).

Password-reset codes are printed in the API console (email sending is a dev stub).

### Mobile app
1. The map uses OpenStreetMap (Mapsui): no API key or billing needed, just internet on the phone/emulator.
2. Start the API, then run `PawRescue.Mobile` on the Android emulator. It calls `http://10.0.2.2:5080` (the host PC).
3. On a real phone, change `ApiConfig.BaseUrl` in `Services/Infrastructure.cs` to your PC's LAN IP and set `Storage:PublicBaseUrl` in the API to the same IP so photos load.

### HOW TO RUN ANDROID EMULATOR
Window 1: & "C:\Program Files (x86)\Android\android-sdk\emulator\emulator.exe" -avd PawPhone
Window 2: cd into PawRescue.Api → dotnet run
Window 3: cd into PawRescue.Mobile → dotnet build -t:Run -f net10.0-android

