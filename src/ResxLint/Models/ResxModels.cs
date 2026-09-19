namespace ResxLint.Models;

record LintRequest(
    string ProjectDir,
    string ResxFile,
    bool WhatIf = false,
    bool FailOnWarnings = false,
    string? ConnectionId = null
);

record LintResult(
    string SessionId,
    int TotalKeys,
    int LanguageCount,
    string BaseResx,
    string[] Languages,
    List<LintIssue> Issues,
    LintSummary Summary
);

record LintIssue(
    string Code,
    string Severity,
    string File,
    int Line,
    string Key,
    string Message,
    string[]? SimilarKeys = null,
    bool CanAutoFix = false,
    string? FixDescription = null
);

record LintSummary(
    int FatalErrors,
    int Warnings,
    int Infos,
    int AutoFixesApplied,
    int Placeholders,
    int MissingTranslations
);

record ResxTranslationData(
    string BaseFile,
    ResxLanguageInfo[] Languages,
    ResxKeyEntry[] Keys
);

record ResxLanguageInfo(
    string Code,
    string FileName,
    int TotalKeys,
    int MissingKeys,
    int Placeholders
);

record ResxKeyEntry(
    string Name,
    string BaseValue,
    Dictionary<string, ResxTranslationValue> Translations
);

record ResxTranslationValue(
    string Value,
    string Status
);

record SaveTranslationRequest(
    string ResxFile,
    string Language,
    Dictionary<string, string> Updates
);

record AiTranslateRequest(
    string Provider,
    string ApiKey,
    string[] Texts,
    string SourceLang,
    string TargetLang,
    string? CustomEndpoint = null,
    string? CustomModel = null
);

record AiProviderInfo(
    string Id,
    string Name,
    string Endpoint,
    string Model,
    string? DocsUrl = null,
    bool Recommended = false
);

record AiTranslateResult(
    bool Success,
    string[]? Translations,
    string? Error
);

record ProjectResxInfo(
    string ProjectName,
    string RelativeFolder,
    string ResxFile,
    string BaseName,
    string[] Languages,
    string[] LanguageFiles
);

record AddKeyRequest(
    string ResxFile,
    string Key,
    string? Value,
    Dictionary<string, string> Translations,
    bool Update = false,
    bool WhatIf = false
);

record AddKeyResult(
    bool Success,
    string? Error,
    string? Key = null,
    string? Value = null,
    List<string>? FilesTouched = null,
    List<string>? LanguageResults = null,
    bool WasUpdate = false
)
{
    public static AddKeyResult Fail(string error) => new(false, error);

    public static AddKeyResult Ok(string key, string value, List<string> filesTouched, List<string> languageResults, bool wasUpdate) =>
        new(true, null, key, value, filesTouched, languageResults, wasUpdate);
}

record RemoveKeyRequest(
    string ResxFile,
    string Key,
    string? ProjectDir = null,
    bool Force = false,
    bool WhatIf = false
);

record RemoveKeyResult(
    bool Success,
    string? Error,
    string? Key = null,
    List<string>? FilesTouched = null,
    string? MatchNote = null
)
{
    public static RemoveKeyResult Fail(string error) => new(false, error);

    public static RemoveKeyResult Ok(string key, List<string> filesTouched, string? matchNote) =>
        new(true, null, key, filesTouched, matchNote);
}

record ProgressMessage(
    string SessionId,
    int Step,
    int TotalSteps,
    string Message,
    string Severity
);
