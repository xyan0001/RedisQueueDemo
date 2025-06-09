using StackExchange.Redis;

namespace TerminalManagementService.Services
{
    // Wrapper for blocking Redis connection (used for blocking operations like BLPOP)
    public class BlockingRedisConnection
    {
        public ConnectionMultiplexer Connection { get; }
        public BlockingRedisConnection(ConnectionMultiplexer connection) => Connection = connection;
    }

    // Wrapper for non-blocking Redis connection (used for non-blocking operations like RPUSH, status updates, etc.)
    public class NonBlockingRedisConnection
    {
        public ConnectionMultiplexer Connection { get; }
        public NonBlockingRedisConnection(ConnectionMultiplexer connection) => Connection = connection;
    }
}
