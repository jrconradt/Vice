namespace Vice.Options;

[ViceOption]
public sealed record FormatOption()
    : ValueBearingOption("format",
                         "Output or content format (text|hex|json|...)",
                         new[]
                         {
                             "auto", "text", "hex", "json", "jsonl", "ndjson",
                             "epub", "html", "xml", "pdf", "txt",
                             "fasta", "gff", "pdb", "cif", "mmcif", "bcif", "pae",
                         },
                         "auto");
