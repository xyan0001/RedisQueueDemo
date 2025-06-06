# Terminal Management System

A scalable, Redis-based system for managing terminal allocations in a high-throughput environment using ASP.NET Core and Kubernetes.

## System Overview

This system addresses the following requirements:

- Management of a fixed number of terminals (initially 40, expandable)
- Handling high throughput (40,000 requests per minute / 667 QPS)
- Redis-based terminal availability management
- Terminal session lifecycle management (login, reuse, expiration)
- Kubernetes deployment with kustomize
- Dynamic expansion capability

## Architecture

### Components

1. **TerminalManagementService**: ASP.NET Core Web API that provides terminal allocation and management
2. **Redis**: Central storage for terminal information, status, and session data
3. **Kubernetes**: Container orchestration for deployment and scaling

### Redis Data Structure

| Redis Key | Type | Purpose |
|-----------|------|---------|
| terminal:info:\<id\> | Hash | Static terminal information (URL, port, credentials) |
| terminal:status:\<id\> | Hash | Dynamic status (available/in-use, pod_id, last_used_time) |
| terminal:session:\<id\> | String | Session ID with auto-expiration (TTL: 300s) |
| terminal_pool | Set | Available terminal IDs for quick allocation |

## Features

### Terminal Initialization

When the application starts, terminals are initialized with Redis commands:

```redis
# For each terminal (e.g., terminal-001)
HSET terminal:info:terminal-001 id "terminal-001" url "example.com" port 22 username "user1" password "pass1"
HSET terminal:status:terminal-001 status "available" pod_id "" last_used_time 1717689600
SADD terminal_pool terminal-001
```

The system:
1. Reads terminal configuration from appsettings.json
2. Creates terminal records in Redis with `HSET` commands
3. Adds terminal IDs to the available pool with `SADD`

### Terminal Allocation/Release

#### Allocation Process

```redis
# Atomically pop a terminal from the pool
SPOP terminal_pool                                 # Returns "terminal-001"

# Update status to in-use
HSET terminal:status:terminal-001 status "in_use" pod_id "pod-abc123" last_used_time 1717689700

# Get terminal information
HGETALL terminal:info:terminal-001                 # Returns all terminal details
```

The `SPOP` command atomically removes and returns a random element from the set, preventing race conditions.

#### Session Management

```redis
# Check if session exists
GET terminal:session:terminal-001                  # Returns session ID or nil

# If nil, login and create session
SET terminal:session:terminal-001 "session-xyz" EX 300  # Create with 5-minute TTL

# If exists, refresh session TTL
EXPIRE terminal:session:terminal-001 300           # Reset TTL to 5 minutes
```

During active usage, the pod periodically updates the last_used_time:

```redis
HSET terminal:status:terminal-001 last_used_time 1717689730
```

#### Terminal Release

```redis
# Atomic release using transaction
MULTI
HSET terminal:status:terminal-001 status "available" pod_id "" last_used_time 1717689800
SADD terminal_pool terminal-001
EXEC
```

The `MULTI`/`EXEC` commands create a Redis transaction to ensure both operations (status update and adding back to pool) happen atomically.

### Crash Recovery

The system automatically detects and recovers from pod crashes:

```redis
# For each terminal status key
SCAN 0 MATCH terminal:status:* COUNT 1000          # Efficiently iterate through keys
HGETALL terminal:status:terminal-002               # Get status, pod_id, last_used_time

# If status is "in_use" and last_used_time is too old (e.g., >30s ago)
MULTI
HSET terminal:status:terminal-002 status "available" pod_id "" last_used_time 1717689950
SADD terminal_pool terminal-002
EXEC
```

1. A background service periodically scans all terminals with `SCAN`
2. Terminals with old `last_used_time` (> 30s) are considered orphaned
3. Orphaned terminals are reclaimed with a transaction

### Graceful Shutdown

When pods are terminated:

```redis
# Get all terminals used by this pod
KEYS terminal:status:*                             # Get all status keys (SCAN in production)

# For each key, check if used by this pod
HGET terminal:status:terminal-001 pod_id           # Check if "pod-abc123"

# For each terminal used by this pod
MULTI
HSET terminal:status:terminal-001 status "available" pod_id "" last_used_time 1717689900
SADD terminal_pool terminal-001
EXEC
```

1. The preStop lifecycle hook provides time for cleanup
2. All terminals allocated to the terminating pod are released
3. Session data remains in Redis until natural expiration

### Dynamic Terminal Expansion

Adding new terminals to the pool:

```redis
# For each new terminal (e.g., terminal-041)
HSET terminal:info:terminal-041 id "terminal-041" url "example.com" port 22 username "user41" password "pass41"
HSET terminal:status:terminal-041 status "available" pod_id "" last_used_time 1717690000
SADD terminal_pool terminal-041
```

1. New terminals can be added through the Admin API
2. Existing pods discover new terminals through the `terminal_pool` set
3. No service disruption during expansion

## Scaling Calculations

- Each request takes 200ms to process
- Each terminal can handle 5 requests per second (1000ms ÷ 200ms)
- Required capacity: 667 QPS
- Minimum terminals needed: 667 ÷ 5 = 134 terminals

The system is initially configured with 40 terminals but can be expanded using the Admin API.

## API Endpoints

### Terminal Management

- `POST /api/terminals/allocate`: Allocates a terminal from the pool
- `POST /api/terminals/release/{id}`: Releases a terminal back to the pool
- `POST /api/terminals/session/{id}/refresh`: Refreshes a terminal session

### Administration

- `POST /api/admin/terminals/add`: Adds new terminals to the pool
- `POST /api/admin/terminals/cleanup`: Forces cleanup of orphaned terminals

## Deployment

### Prerequisites

- Kubernetes cluster
- kubectl and kustomize installed
- Docker for building images

### Deployment Steps

1. Build the Docker image:
   ```
   docker build -t terminal-management:latest .
   ```

2. Apply Kubernetes configuration:
   ```
   kubectl apply -k k8s/overlays/production
   ```

### Configuration

Terminal settings can be configured in the Kubernetes ConfigMap:

- URL and port for terminals
- Username and password patterns
- Initial terminal count
- Session timeout settings

## Monitoring and Health

The service includes:

- Health endpoint at `/health`
- Kubernetes readiness/liveness probes
- Proper resource limits and requests