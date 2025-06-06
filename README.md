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

The system initializes terminals from configuration at startup:

1. Reads terminal configuration from appsettings.json
2. Creates terminal records in Redis with status "available"
3. Adds terminals to the available pool

### Terminal Allocation/Release

The process for terminal usage is:

1. **Allocation**: 
   - Pop a terminal ID from the available pool
   - Mark it as in-use with the pod ID
   - Record the last used time

2. **Session Management**:
   - Check for existing session
   - If none exists, perform login and create session
   - Refresh TTL to prevent expiration

3. **Release**:
   - Mark terminal as available
   - Add it back to the pool
   - Update last used time

### Crash Recovery

The system automatically detects and recovers from pod crashes:

1. A background service periodically checks for terminals marked as in-use
2. If the last_used_time is older than the threshold (30s by default), the terminal is considered orphaned
3. Orphaned terminals are reclaimed and added back to the pool

### Graceful Shutdown

When pods are terminated:

1. The preStop lifecycle hook provides time for in-flight requests to complete
2. The service releases all terminals allocated to the terminating pod
3. Session data is preserved in Redis for potential reuse

### Dynamic Terminal Expansion

Terminals can be added dynamically:

1. Use the Admin API to add new terminals
2. New pods automatically discover all available terminals
3. No downtime during expansion

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