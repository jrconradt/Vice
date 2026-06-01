namespace Vice.Net.Requests.Grpc;

internal readonly record struct GrpcMethodPath(string ServiceName, string MethodName)
{
    public static bool TryParse(string method, out GrpcMethodPath path)
    {
        var slash = method.LastIndexOf('/');
        if (slash <= 0
            || slash == method.Length - 1)
        {
            path = default;
            return false;
        }

        path = new GrpcMethodPath(method[..slash], method[(slash + 1)..]);
        return true;
    }
}
