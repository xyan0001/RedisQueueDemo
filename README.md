# Terminal Management System

A scalable, Redis-based system for managing terminal allocations in a high-throughput environment using ASP.NET Core and Kubernetes. This system includes advanced terminal information caching for optimal performance.

## System Overview

This system addresses the following requirements:

- Management of a fixed number of terminals (initially 40, expandable)
- Handling high throughput (40,000 requests per minute / 667 QPS)
- Redis-based terminal availability management
- Terminal session lifecycle management (login, reuse, expiration)
- In-memory caching of terminal information for performance optimization
- Kubernetes deployment with kustomize
- Dynamic expansion capability

## Architecture

### Components

1. **TerminalManagementService**: ASP.NET Core Web API that provides terminal allocation and management
2. **Redis**: Central storage for terminal information, status, and session data
3. **Kubernetes**: Container orchestration for deployment and scaling

### Performance Optimizations

#### Terminal Information Caching

To reduce Redis load and improve performance:

1. Static terminal information is cached in-memory using `ConcurrentDictionary<string, TerminalInfo>`
2. Cache is initialized during application startup
3. Thread-safe operations are ensured through `ConcurrentDictionary` APIs

### Redis Data Structure

| Redis Key | Type | Purpose |
|-----------|------|---------|
| terminal:status:<id> | Hash | Dynamic status (available/in-use, pod_id, last_used_time) |
| terminal:session:<id> | String | Session ID with auto-expiration (TTL: 300s) |
| terminal_queue | List | Available terminal IDs for quick allocation |

Why Redis List?

Redis List provides efficient atomic operations for terminal allocation and release, ensuring that multiple pods can safely allocate terminals.

- Synchronous blocking: Requests block if no terminals are available, waiting until a terminal is released.
- High performance: `BLPOP` is a native Redis command, extremely efficient and capable of handling a large number of concurrent requests.
- Instant availability: As soon as a terminal is released, a waiting request is immediately awakened—no polling required.
- Simple and reliable: Redis ensures atomicity, making it a natural fit for managing terminal availability without complex locking mechanisms.

> **Note:** The system now uses a Redis List (`terminal_queue`) for terminal allocation and release. All allocation uses `BLPOP`, and release uses `RPUSH`.

## Features

### Terminal Initialization

When the application starts, terminals are initialized from configuration and added to Redis:

```redis
# For each terminal (e.g., terminal-001)
HSET terminal:status:terminal-001 TerminalId "terminal-001" Status "available" PodName "" LastUsedTime 1717689600
RPUSH terminal_queue terminal-001
```

The system:

1. Reads terminal configuration from appsettings.json
2. Creates terminal status hashes in Redis
3. Adds terminal IDs to the available queue with `RPUSH`

### Terminal Allocation/Release

#### Allocation Process

```redis
# Atomically pop a terminal from the queue
BLPOP terminal_queue 0 # Blocks until a terminal is available

# Update status to in-use
HSET terminal:status:terminal-001 Status "in_use" PodName "pod-abc123" LastUsedTime 1717689700
```

#### Release Process

```redis
# Update status to available
HSET terminal:status:terminal-001 Status "available" PodName "" LastUsedTime 1717689800
# Add back to queue
RPUSH terminal_queue terminal-001
```

### Session Management

```redis
# Check if session exists
GET terminal:session:terminal-001
# If nil, create session with TTL
SET terminal:session:terminal-001 "session-xyz" EX 300
# Refresh session TTL
EXPIRE terminal:session:terminal-001 300
```

### Crash Recovery & Orphan Reclamation

- The system periodically scans all terminal status hashes.
- If a terminal is marked in-use but has not been used for a threshold period, it is reclaimed:

```redis
HSET terminal:status:terminal-001 Status "available" PodName "" LastUsedTime 1717689950
RPUSH terminal_queue terminal-001
```

### Graceful Shutdown

- On shutdown, all terminals allocated to the current pod are released using the same logic as above.

### Dynamic Terminal Expansion

- New terminals can be added by updating configuration and re-initializing, or via the Admin API.

## Redis Connection Architecture

- The system uses two separate Redis connections:
  - **Default connection**: Used for allocation (BLPOP) and most operations.
  - **Release connection**: Dedicated for release operations (RPUSH) to avoid deadlocks and connection starvation under high concurrency.
- This ensures that even if all allocation threads are blocked, releases can still proceed.

## API Endpoints

### Terminal Management

- `POST /api/terminals/allocate`: Allocates a terminal from the pool and returns terminal details with a session ID
- `POST /api/terminals/release/{id}`: Releases a terminal back to the pool
- `POST /api/terminals/cleanup`: Cleans up orphaned/inactive terminals (admin/maintenance)
- `GET /api/terminals/statuses`: Returns the status of all terminals (operational/diagnostic endpoint)
- `GET /api/terminals/simulate-single-lifecycle`: Simulates a full allocation/use/release lifecycle (for testing)
- `GET /api/terminals/lifecycle-simulation?iterations={int}&parallelism={int}`: Runs a configurable lifecycle simulation with specified iterations and parallelism (for load testing)

## Operational Notes

- **Concurrency Limit**: Do not exceed the number of available terminals with concurrent allocations. The system will block on allocation if all terminals are in use.
- **Deadlock Prevention**: The dual Redis connection design ensures that releases are always possible, even under heavy load.
- **Redis Key Expiry**: Terminal status hashes and sessions have TTLs and will expire if not updated.
- **Monitoring**: Use the provided endpoints and Redis commands to monitor queue length, terminal status, and cache performance.

## Redis Commands (Quick Reference)

- List all terminals in the queue:

  ```redis
  LRANGE terminal_queue 0 -1
  ```

- Check queue length:

  ```redis
  LLEN terminal_queue
  ```

- Release a terminal manually:

  ```redis
  RPUSH terminal_queue <id>
  ```

- Allocate a terminal manually:

  ```redis
  BLPOP terminal_queue 0
  ```

- Check all keys of terminal status:

  ```redis
  KYES terminal:status:*
  ```

- Check terminal status:

  ```redis
  HGETALL terminal:status:<id>
  ```

- Check a terminal status by ID:

  ```redis
  HGET terminal:status:<id> Status
  ```

- Check all session keys:

  ```redis
  KEYS terminal:session:*
  ```

- Check a session by ID:

  ```redis
  GET terminal:session:<id>
  ```