# Qubic Doge Stats

Real-time mining statistics dashboard for **Qubic × Dogecoin** mining operations.

Data is polled every 60 seconds from the public [doge-stats.qubic.org](https://doge-stats.qubic.org) API and stored locally. A Blazor WebAssembly frontend visualizes hashrate, pool stats, epoch history, and more.

---

## Features

- Live hashrate monitoring (GH/s) with 60-minute trend chart
- Pool stats: accepted / rejected / submitted shares
- Solutions tracking: accepted, received, rejected, stale
- Connected peers and active tasks
- Qubic epoch tracking with automatic epoch-change detection
- Epoch comparison charts (hashrate & solutions per epoch)
- Current epoch timeline chart (day-by-day breakdown)
- Today's hashrate by hour
- Countdown to the next epoch transition
- Light / dark mode
- Fully self-hosted via Docker

---

## Architecture

```
External APIs
  https://doge-stats.qubic.org/dispatcher.json   (mining stats)
  https://rpc.qubic.org/v1/tick-info             (Qubic epoch)
           │                         │
     DogeStatsClient          QubicRpcClient
           └──────────┬────────────┘
                      │
          DogeStatsPollingWorker
          (Background service, every 60 s)
                      │
               HashrateSnapshot
                      │
            LiteDB  (doge_stats.db)
                      │
         ┌────────────┴────────────┐
         │                         │
  Minimal API (ASP.NET)     Blazor WASM (Client)
  /api/snapshots/latest     Home.razor (Dashboard)
  /api/snapshots/history    StatCard, CountdownBlock
```

---

## API Endpoints

Base URL (local): `http://localhost:5159`

### `GET /api/snapshots/latest`

Returns the most recent stored snapshot.

**Response `200 OK`:**
```json
{
  "id": "...",
  "timestamp": "2026-03-31T12:00:00+00:00",
  "hashrate": 1234567890,
  "hashrateDisplay": "1.23 GH/s",
  "poolDifficulty": 100000,
  "tasksDistributed": 512,
  "activeTasks": 48,
  "connectedPeers": 312,
  "totalPeers": 450,
  "poolAccepted": 98234,
  "poolRejected": 12,
  "poolSubmitted": 98246,
  "solutionsAccepted": 7412,
  "solutionsReceived": 7415,
  "solutionsRejected": 3,
  "solutionsStale": 0,
  "uptimeSeconds": 864000,
  "queueSolutions": 0,
  "queueStratum": 2,
  "qubicEpoch": 180
}
```

**Response `404 Not Found`** — no data collected yet.

---

### `GET /api/snapshots/history?limit=100`

Returns the most recent N snapshots, ordered by time (newest first).

| Parameter | Type | Default | Max | Description |
|-----------|------|---------|-----|-------------|
| `limit` | `int` | `100` | `10080` | Number of snapshots to return (10 080 = ~7 days at 1/min) |

**Response `200 OK`:** Array of `HashrateSnapshot` objects (same schema as above).

---

## Data Model — `HashrateSnapshot`

| Field | Type | Source | Description |
|-------|------|--------|-------------|
| `timestamp` | `DateTimeOffset` | Server | UTC time of the poll |
| `hashrate` | `long` | `mining.hashrate` | Raw hashrate in H/s |
| `hashrateDisplay` | `string` | `mining.hashrate_display` | Human-readable (e.g. "1.23 GH/s") |
| `poolDifficulty` | `long` | `mining.pool_difficulty` | Current pool difficulty |
| `tasksDistributed` | `int` | `mining.tasks_distributed` | Total tasks distributed |
| `activeTasks` | `int` | `active_tasks` | Currently active tasks |
| `connectedPeers` | `int` | `network.connected_peers` | Active peer connections |
| `totalPeers` | `int` | `network.total_peers` | Total known peers |
| `poolAccepted` | `int` | `pool.accepted` | Pool shares accepted |
| `poolRejected` | `int` | `pool.rejected` | Pool shares rejected |
| `poolSubmitted` | `int` | `pool.submitted` | Pool shares submitted |
| `solutionsAccepted` | `int` | `solutions.accepted` | Solutions accepted by network |
| `solutionsReceived` | `int` | `solutions.received` | Solutions received by pool |
| `solutionsRejected` | `int` | `solutions.rejected` | Solutions rejected |
| `solutionsStale` | `int` | `solutions.stale` | Stale solutions |
| `uptimeSeconds` | `long` | `uptime_seconds` | Node uptime |
| `queueSolutions` | `int` | `queues.solutions` | Solutions in queue |
| `queueStratum` | `int` | `queues.stratum` | Stratum connections in queue |
| `qubicEpoch` | `int` | `rpc.qubic.org` | Current Qubic epoch number |

---

## Epoch Tracking

A new Qubic epoch begins every **Wednesday at 12:00 UTC**. The polling worker:

- Refreshes the epoch from `rpc.qubic.org/v1/tick-info` every **5 minutes** normally
- During the **Wednesday 11:00–15:00 UTC** transition window: refreshes on **every poll** to catch the changeover as quickly as possible
- Logs an info message when an epoch change is detected

---

## Configuration

All settings can be overridden via `appsettings.json`, environment variables, or Docker environment.

| Key | Default | Description |
|-----|---------|-------------|
| `DogeStats:ApiUrl` | `https://doge-stats.qubic.org/dispatcher.json` | Mining stats source |
| `DogeStats:PollIntervalSeconds` | `60` | Poll interval in seconds |
| `QubicRpc:BaseUrl` | `https://rpc.qubic.org/` | Qubic RPC base URL |
| `LiteDb:Filename` | `Data/doge_stats.db` | LiteDB database file path |

**Docker environment variables:**

| Variable | Description |
|----------|-------------|
| `DATA_DIR` | Directory for the LiteDB file (e.g. `/data`) |
| `LITEDB_FILE` | DB filename inside `DATA_DIR` (e.g. `doge_stats.db`) |
| `ASPNETCORE_URLS` | Listening URL (default `http://+:8080`) |

---

## Running Locally

**Prerequisites:** .NET 10 SDK

```bash
git clone https://github.com/AndyQus/qubic_doge_stats.git
cd qubic_doge_stats/qubic_doge_stats
dotnet run
```

Open `http://localhost:5159` in your browser.

---

## Docker Deployment

**Prerequisites:** Docker

```bash
# Build the image
docker build -t qubic_doge_stats .

# Run with a persistent volume
docker run -d \
  --name qubic_doge_stats \
  -p 8080:8080 \
  -v qubic_doge_data:/data \
  -e DATA_DIR=/data \
  -e LITEDB_FILE=doge_stats.db \
  --restart unless-stopped \
  qubic_doge_stats
```

The app will be available at `http://localhost:8080`.

The database is persisted in a named Docker volume (`qubic_doge_data`).

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Runtime | .NET 10 / ASP.NET Core |
| Frontend | Blazor WebAssembly |
| UI Components | MudBlazor 9.x |
| Database | LiteDB 5.x (embedded, file-based) |
| Container | Docker / Docker Compose |

---

## External Data Sources

| Source | URL | Description |
|--------|-----|-------------|
| DogeStats API | `https://doge-stats.qubic.org/dispatcher.json` | Mining pool statistics |
| Qubic RPC | `https://rpc.qubic.org/v1/tick-info` | Current epoch / tick info |

---

## Disclaimer

This application is in **beta**. All data is retrieved from public endpoints.
Completeness, accuracy, and availability of the captured data are not guaranteed.
Use it for demonstration and analysis purposes only.
