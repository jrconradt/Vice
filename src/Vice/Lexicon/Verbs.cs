using Vice.Nodes;
using static Vice.Dsl;

namespace Vice.Lexicon;

public static class Verbs
{
    public static ChainNode Tcp() => verb("tcp", "tcpcat");
    public static ChainNode Udp() => verb("udp");
    public static ChainNode Grpc() => verb("grpc", "grpcurl");
    public static ChainNode Search() => verb("search", "find");
    public static ChainNode Fetch() => verb("fetch", "get");
    public static ChainNode Download() => verb("download", "dl");
    public static ChainNode Archive() => verb("archive");
    public static ChainNode Read() => verb("read");
    public static ChainNode Write() => verb("write");
    public static ChainNode Append() => verb("append");
    public static ChainNode Stream() => verb("stream");
    public static ChainNode Unarchive() => verb("unarchive");
    public static ChainNode Build() => verb("build");
    public static ChainNode Test() => verb("test");
    public static ChainNode Restore() => verb("restore");
    public static ChainNode Clean() => verb("clean");
    public static ChainNode Help() => verb("help");
    public static ChainNode Version() => verb("version");
    public static ChainNode List() => verb("list");
    public static ChainNode Daemon() => verb("daemon");
    public static ChainNode Status() => verb("status");
    public static ChainNode Manpage() => verb("manpage");
    public static ChainNode Completions() => verb("completions");
    public static ChainNode Exit() => verb("exit", "quit");
    public static ChainNode Jobs() => verb("jobs");
    public static ChainNode Pause() => verb("pause");
    public static ChainNode Resume() => verb("resume");
    public static ChainNode Cancel() => verb("cancel");
    public static ChainNode History() => verb("history");
    public static ChainNode Clear() => verb("clear");
    public static ChainNode Set() => verb("set");
    public static ChainNode Cache() => verb("cache");
    public static ChainNode Inspect() => verb("inspect");
    public static ChainNode Split() => verb("split");
    public static ChainNode Route() => verb("route");
    public static ChainNode Tee() => verb("tee");
    public static ChainNode Strategies() => verb("strategies");
}
