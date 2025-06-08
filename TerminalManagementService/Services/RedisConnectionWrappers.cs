using StackExchange.Redis;

namespace TerminalManagementService.Services
{
    // Wrapper for default Redis connection
    public class DefaultRedisConnection
    {
        public ConnectionMultiplexer Connection { get; }
        public DefaultRedisConnection(ConnectionMultiplexer connection) => Connection = connection;
    }

    // Wrapper for release Redis connection
    public class ReleaseRedisConnection
    {
        public ConnectionMultiplexer Connection { get; }
        public ReleaseRedisConnection(ConnectionMultiplexer connection) => Connection = connection;
    }
}
