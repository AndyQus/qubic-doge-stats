# Qubic Doge Stats

Real-time mining statistics tracker for Qubic x Dogecoin mining operations.

## How it works

### Architecture

```
External API (doge-stats.qubic.org)   Qubic RPC (rpc.qubic.org)
         ↓                                      ↓
    DogeStatsClient                      QubicRpcClient
         ↓                                      ↓
           DogeStatsPollingWorker (every 60s)
                        ↓
              HashrateSnapshot (+ QubicEpoch)
                        ↓
               LiteDB (doge_stats.db)
                        ↓
         ┌──────────────┴─────────────────┐
         ↓                                ↓
   Backend API (Minimal APIs)    Blazor WASM Frontend
   /api/snapshots/latest         Home.razor (dashboard)
   /api/snapshots/history        StatCard, CountdownBlock
```

### Data Collection

The `DogeStatsPollingWorker` background service polls every 60 seconds:
1. Fetches mining stats from `https://doge-stats.qubic.org/dispatcher.json`
2. Fetches current Qubic epoch from `https://rpc.qubic.org/v1/tick-info`
   - Normal operation: epoch refreshed every 5 minutes
   - Wednesday 11:00–15:00 UTC (epoch transition window): refreshed every poll
3. Stores `HashrateSnapshot` with all fields + `QubicEpoch` to LiteDB

### Epoch Tracking

A new Qubic epoch begins every Wednesday at 12:00 UTC. The epoch transition
can take some time — the RPC API reflects the new epoch once the network completes
the handover. The worker detects epoch changes and logs them.

### Frontend

Blazor WebAssembly app that fetches data from the backend API:
- **Countdown** — shows time until April 1, 2026 12:00 UTC (mining start)
- **Stats Cards** — hashrate, pool accepted/rejected, peers, solutions, uptime
- **Hashrate Chart** — last 60 data points as a line chart
- **Footer** — disclaimer, links, version

### Configuration

| Key | Default | Description |
|-----|---------|-------------|
| `DogeStats:ApiUrl` | `https://doge-stats.qubic.org/dispatcher.json` | Mining stats API |
| `DogeStats:PollIntervalSeconds` | `60` | Poll interval in seconds |
| `QubicRpc:BaseUrl` | `https://rpc.qubic.org/` | Qubic RPC base URL |
| `LiteDb:Filename` | `Data/doge_stats.db` | LiteDB file path |

Environment variable overrides: `DATA_DIR`, `LITEDB_FILE`

---

## Maßnahmenplan / Backlog

### In Arbeit
- [x] Grundstruktur: Polling, LiteDB, Blazor UI, MudBlazor
- [x] Hashrate-Chart (letzte 60 Messpunkte)
- [x] Countdown bis Mining-Start (1. April 2026, 12:00 UTC)
- [x] Qubic Epoch je Datensatz speichern (rpc.qubic.org/v1/tick-info)
- [x] Epoch-Transition-Erkennung (Mittwoch 11–15 UTC: erhöhte Prüffrequenz)
- [x] FooterComponent mit Disclaimer, Links, Version

### Offen / Geplant

#### Daten & Backend
- [ ] **Epoch-Statistiken**: Aggregierte Werte pro Epoche (Gesamt-Solutions, Hashrate-Durchschnitt, etc.)
- [ ] **Poll-Intervall konfigurierbar per UI** (z.B. nach Mining-Start auf 5 min umstellen)
- [ ] **Datenbereinigung**: Alte Snapshots automatisch löschen (z.B. >30 Tage)
- [ ] **API-Endpoint für Epoch-Übersicht**: `/api/epochs` mit Aggregaten pro Epoche

#### Frontend / UI
- [ ] **Epoch-Anzeige** in den Stats-Cards oder Header (aktuell: Epoche X)
- [ ] **Epoch-Vergleich**: Stats dieser Epoche vs. letzter Epoche
- [ ] **Solutions-pro-Stunde Chart**: Ergänzend zum Hashrate-Chart
- [ ] **Peers-Chart**: ConnectedPeers über Zeit
- [ ] **Datum/Uhrzeit der letzten Epoch-Änderung** anzeigen
- [ ] **Mobile-Optimierung**: StatCards bei xs kompakter darstellen
- [ ] **Logo** in Footer und AppBar einbinden (logos/Logo_dark.webp)

#### Infrastruktur
- [ ] **Docker Compose** mit Volume-Mount für persistente DB
- [ ] **Health-Check Endpoint** (`/health`)
- [ ] **Swagger/OpenAPI** für Backend-API
- [ ] **Logging strukturiert** (Seq oder ähnlich)
- [ ] **Unit-Tests** für Polling-Logik und Epoch-Transition-Erkennung

---

## Tech Stack

- .NET 9 / ASP.NET Core
- Blazor WebAssembly (WASM)
- MudBlazor 8.x
- LiteDB 5.x (embedded)
- Docker
