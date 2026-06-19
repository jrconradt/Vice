# Network commands

Verbs for raw TCP, UDP, and gRPC. Endpoints are always `host:port`. Output is written to stdout by default; format and encoding are governed by the global options listed at the end.

## tcp / tcpcat

Open a TCP connection, send a payload, half-close the send side, and print the server's response. Default timeout 30 s; max response 16 MiB.

### Synopsis

```
vice tcp send "<data>" to endpoint <host:port>
vice tcp send file <path> to endpoint <host:port>
```

`tcpcat` is a synonym for `tcp`.

### Example

```bash
vice tcp send "PING" to endpoint localhost:6379
```

Payloads are sent verbatim — escape sequences like `\r\n` are not interpreted. For protocols that need CRLF, pipe the bytes in from a file with `tcp send file <path>`.

## udp

Send a UDP datagram and (by default) wait for one reply. Default timeout 5 s. Use `--no-reply` for fire-and-forget.

### Synopsis

```
vice udp send "<data>" to endpoint <host:port>
vice udp send file <path> to endpoint <host:port>
```

### Example

```bash
vice udp send "ping" to endpoint localhost:5353 --no-reply
```

## grpc / grpcurl

Talks to any gRPC server that exposes [server reflection](https://github.com/grpc/grpc/blob/master/doc/server-reflection.md). Three sub-verbs: `list`, `describe`, `call`. `grpcurl` is a synonym for `grpc`.

### list services

Enumerate the services exposed via reflection.

```
vice grpc list services on endpoint <host:port>
```

```bash
vice grpc list services on endpoint localhost:50051
```

### describe service

Print each `rpc` in a service, including `stream` annotations.

```
vice grpc describe service <fully.qualified.Service> on endpoint <host:port>
```

```bash
vice grpc describe service helloworld.Greeter on endpoint localhost:50051
```

### call

Invoke a unary, server-streaming, client-streaming, or duplex-streaming method. Request and response bodies are JSON. Method type is auto-detected via reflection.

```
vice grpc call <package.Service/Method> on endpoint <host:port> with data '<json>'
```

```bash
vice grpc call helloworld.Greeter/SayHello on endpoint localhost:50051 with data '{"name":"World"}'
```

For client-streaming methods, pass a JSON array; each element becomes one message.

TLS (`https://`) is used for every endpoint regardless of port. Use `--plaintext` to force cleartext (`http://`).

## Global options

Network verbs honor the following global options (set as `--name value` or `--flag`):

| Option | Default | Applies to | Effect |
|---|---|---|---|
| `--timeout <ms>` | TCP 30000, UDP 5000 | tcp, udp, grpc | Per-call socket timeout. |
| `--format <text\|hex\|json>` | `text` | tcp, udp, grpc | Output rendering for response bytes. |
| `--encoding <utf8\|ascii>` | `utf8` | tcp, udp, grpc | Encoding used to decode request text and render `text` output. |
| `--no-reply` | off | udp | Don't wait for a UDP reply; send and return. |
| `--plaintext` | off | grpc | Force `http://` (otherwise TLS is used for every endpoint). |
| `--metadata '<json>'` | (none) | grpc call | Headers as a JSON object of string→string. |
