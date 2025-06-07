# Architectural Changes in Terminal Management Service

## Overview

The Terminal Management Service has been refactored to improve its architecture, focusing on better separation of concerns, configuration management, and deployment strategies. This document outlines the key changes and their rationale.

## Key Changes

### 1. Strong-Typed Terminal Status

- Implemented a consistent use of the `TerminalStatus` class throughout the codebase
- Replaced string-based status flags with strongly-typed constants
- Added proper validation and error handling for status operations

### 2. Redis State Management

- **Before**: Both static terminal configuration and dynamic state were stored in Redis
- **After**: Only dynamic state (status, allocation, timestamps) is stored in Redis
- Benefits:
  - Reduced Redis memory footprint
  - Improved startup performance
  - Better separation of configuration and state

### 3. Configuration Management

- Moved static terminal configuration to `appsettings.json`
- Created environment-specific configuration files
- Added support for Kubernetes ConfigMaps and Secrets
- Benefits:
  - Easier configuration management
  - Better security for sensitive data
  - Simplified deployment across environments

### 4. Environment Detection

- Added automatic environment detection (Local vs. Kubernetes)
- Implemented conditional initialization based on environment
- Benefits:
  - Consistent behavior across development and production
  - Simplified local debugging
  - Proper separation of initialization concerns

### 5. Kubernetes Deployment

- Created a dedicated Redis pool initialization job
- Separated application deployment from data initialization
- Added proper resource limits and health checks
- Benefits:
  - More reliable deployments
  - Better scalability
  - Proper separation of initialization and runtime concerns

### 6. Terminal Data Storage

- TerminalsData is now stored in a ConfigMap for Kubernetes
- Local development uses appsettings.json
- Benefits:
  - Consistent configuration across environments
  - Easier updates of terminal data
  - Better separation of concerns

## Implementation Details

### Redis Keys and Data Structure

- `terminal:status:{id}` - Hash containing dynamic status information
  - `status` - Current status (available/in_use)
  - `pod_name` - Name of the pod that allocated the terminal
  - `last_used_time` - Unix timestamp of last activity
- `terminal:session:{id}` - String containing the session ID with TTL
- `terminal_pool` - Set containing available terminal IDs

### Terminal Lifecycle

1. **Initialization**:
   - In local development: Service initializes Redis on startup
   - In Kubernetes: Dedicated job initializes Redis before deployment

2. **Allocation**:
   - Terminal is atomically popped from the available pool
   - Status is updated with pod information
   - Terminal info is retrieved from configuration or cache

3. **Release**:
   - Status is reset to available
   - Terminal is added back to the available pool

4. **Cleanup**:
   - Background service reclaims orphaned terminals
   - On shutdown, all terminals allocated by the pod are released

## Migration Notes

When deploying a new version with updated terminal data, the initialization job will only add new terminals that don't already exist in Redis. This ensures that existing terminal allocations aren't affected during an update.

## Future Improvements

- Add metrics and monitoring for terminal usage
- Implement rate limiting for terminal allocation
- Add support for terminal grouping and tagging
- Improve session management with better error handling
