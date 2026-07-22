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
    int Placeholders,
    int IdenticalKeys
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
    string TargetLang
);

record AiTranslateResult(
    bool Success,
    string[]? Translations,
    string? Error
);

record ProjectResxInfo(
    string ResxFile,
    string BaseName,
    string[] LanguageFiles
);

record ProgressMessage(
    string SessionId,
    int Step,
    int TotalSteps,
    string Message,
    string Severity
);
