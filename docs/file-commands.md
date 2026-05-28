# File commands

Filesystem reads, writes, streams, archives, and predicate-based search.

## read

Read a file. Standalone, decodes UTF-8 and prints to stdout. When piped, emits raw bytes as a stream of chunks (default 81920 bytes per chunk; override with `--chunk-size <n>`).

### Decompression

`read` transparently inflates these extensions when the file's name ends in them:

| Extension | Codec |
|---|---|
| `.gz` | gzip |
| `.br` | brotli |
| `.deflate` | raw DEFLATE |

Inflated payloads are capped at 8 GiB to defeat decompression-bomb inputs; reads beyond the cap throw `InvalidDataException`.

### Synopsis

```
vice read <path>
vice read <path> then stream to file <dest>
vice read <path> then stream to count
```

### Example

```bash
vice read ./access.log.gz
```

## write

Overwrites the destination with the pipeline's captured stdout as UTF-8. Standalone use is a no-op (writes an empty file); typical use is at the end of a pipe.

### Synopsis

```
vice <producer> then write to file <path>
```

### Example

```bash
vice search "graph" on source arxiv --limit 50 then write to file ./graph-papers.txt
```

## append

Stream consumer. Appends incoming bytes to the file (`FileShare.Read` while writing).

### Synopsis

```
vice <streaming-producer> then append to file <path>
```

### Example

```bash
vice read ./chunk1.bin then append to file ./combined.bin
```

## stream

Three stream consumers:

| Form | Effect |
|---|---|
| `stream to console` | Write bytes to stdout. |
| `stream to file <path>` | Overwrite file with received bytes. |
| `stream to count` | Discard bytes; print chunk count and total byte count. |

### Example

```bash
vice read ./large.bin.gz then stream to count
```

## unarchive

Extract a `.zip`, `.tar`, `.tar.gz`, or `.tgz` archive. Prints the extraction root on success.

### Synopsis

```
vice unarchive <archive>                       # extracts to a temp directory
vice unarchive <archive> to dir <dest>         # extracts to <dest>
```

The destination, if given, must lie inside one of the standard write roots: the user home directory, the XDG `data` / `cache` / `state` directories, the system temp directory, or the current working directory. Destinations outside every root are refused.

## search files / search folders

Walk the filesystem and emit matches by axis-keyed predicate. Up to three AND-combined predicates per query. `in <root>` selects the walk root (defaults to the current directory).

### Synopsis

```
vice search files by <axis> <pattern> [and by <axis2> <pattern2> ...] [in <root>]
vice search folders by <axis> <pattern> [and by <axis2> <pattern2> ...] [in <root>]
vice search for files by <axis> <pattern> ...   # `for` is optional
```

### Axes

| Axis | Pattern syntax | Example |
|---|---|---|
| `name` | glob (or regex with `--regex`); matched against filename only | `name "*.cs"` |
| `path` | glob (or regex with `--regex`); matched against full path | `path "**/tests/**"` |
| `type` | language alias (`csharp`, `python`, `markdown`, ...) or extension | `type csharp` |
| `size` | `>`, `<`, `>=`, `<=`, `=` with bytes; suffixes `B`/`K`/`M`/`G`/`T` (SI) or `Ki`/`Mi`/`Gi`/`Ti` (binary) | `size ">1Mi"` |
| `mtime` | `since <when>` or `before <when>`; `<when>` is an ISO date or a duration like `7d`, `2h`, `30m` | `mtime "since 7d"` |

Full type-alias list lives in the source; common entries include `csharp`, `c#`, `fsharp`, `typescript`, `javascript`, `python`, `rust`, `go`, `java`, `markdown`, `json`, `yaml`, `xml`, `shell`, `pdf`, `image`, `archive`.

### Examples

```bash
vice search files by type csharp in ./src
vice search files by name "*.log" and by size ">10Mi" in ./logs
vice search files by type python and by mtime "since 1d" in .
vice search folders by name "*.git"
```

### Behavior of the file stream

`search files` (no piping) prints matched paths to stdout. When piped, it emits the contents of each matched file as a stream of byte chunks — letting you chain things like:

```bash
vice search files by type markdown in ./docs then stream to count
```

## Global options

| Option | Default | Effect |
|---|---|---|
| `--regex` | off | Treat `name`/`path` patterns as .NET regex instead of glob. |
| `--follow-symlinks` | off | Descend into symlinked directories during walk. |
| `--include-hidden` | off | Include dotfiles and hidden entries. |
| `--depth <n>` | unbounded | Cap walk depth. |
| `--chunk-size <n>` | 81920 | Chunk size in bytes for byte-streaming reads. |
