; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category          | Severity | Notes
--------|-------------------|----------|-------
VICE001 | Vice.Composition  | Info     | No [ViceHost] type found
VICE002 | Vice.Composition  | Error    | Multiple [ViceHost] types
VICE003 | Vice.Composition  | Error    | Parameter cannot be resolved from host
VICE004 | Vice.Composition  | Error    | [ViceCommandPack] has no Register(IViceApp, ...) method
VICE005 | Vice.Composition  | Error    | Duplicate factory return type
VICE006 | Vice.Composition  | Error    | [ViceOption] type is not a usable GlobalOption
VICE010 | Vice.Composition  | Warning  | Cannot infer targets from chain expression
VICE011 | Vice.Composition  | Error    | Multiple registrations for [ViceCommand] disagree on targets
