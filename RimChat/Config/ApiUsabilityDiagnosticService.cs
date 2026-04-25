using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimChat.AI;
using UnityEngine;
using UnityEngine.Networking;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: ApiConfig/LocalModelConfig, UnityWebRequest, and AI debug telemetry.
    /// Responsibility: execute fail-fast API usability diagnostics for cloud/local chat providers.
    /// </summary>
    internal static partial class ApiUsabilityDiagnosticService
    {
        private const int CloudTimeoutSeconds = 12;
        private const int LocalTimeoutSeconds = 8;
        private const string LocalOllamaProbePath = "/api/tags";
        private const string LocalOpenAiModelsPath = "/v1/models";
        private const string LocalOpenAiChatPath = "/v1/chat/completions";
        private const string LocalOllamaGeneratePath = "/api/generate";

        internal static IEnumerator RunCloudDiagnosticCoroutine(
            ApiConfig config,
            Action<ApiUsabilityProgress> onProgress,
            Action<ApiUsabilityDiagnosticResult> onCompleted)
        {
            DateTime startedAtUtc = DateTime.UtcNow;
            var steps = new List<ApiUsabilityStepResult>();

            // Player2 has no models endpoint and selects model server-side;
            // run a simplified diagnostic that only validates chat connectivity.
            if (config?.Provider == AIProvider.Player2)
            {
                yield return RunPlayer2CloudDiagnosticCoroutine(config, onProgress, onCompleted, startedAtUtc, steps);
                yield break;
            }

            const int totalSteps = 6;
            string modelName = config?.GetEffectiveModelName() ?? string.Empty;

            ApiUsabilityDiagnosticResult validationFailure = ValidateCloudConfig(config, startedAtUtc, steps, modelName);
            if (validationFailure != null)
            {
                onCompleted?.Invoke(validationFailure);
                yield break;
            }

            NotifyProgress(onProgress, ApiUsabilityStep.ConfigValidation, 1, totalSteps);
            steps.Add(BuildStepSuccess(ApiUsabilityStep.ConfigValidation, string.Empty, startedAtUtc));
            ApiUsabilityCloudRuntime runtime = ResolveCloudRuntime(config);
            if (!runtime.IsValid)
            {
                onCompleted?.Invoke(BuildFailure(
                    ApiUsabilityStep.RuntimeEndpointResolution,
                    ApiUsabilityErrorCode.ENDPOINT_NOT_FOUND,
                    runtime.Details,
                    0,
                    runtime.ModelsEndpoint,
                    startedAtUtc,
                    steps,
                    modelName,
                    true,
                    null,
                    runtime.Details));
                yield break;
            }

            NotifyProgress(onProgress, ApiUsabilityStep.RuntimeEndpointResolution, 2, totalSteps);
            steps.Add(BuildStepSuccess(ApiUsabilityStep.RuntimeEndpointResolution, runtime.ModelsEndpoint, startedAtUtc));

            NotifyProgress(onProgress, ApiUsabilityStep.ModelsProbe, 3, totalSteps);
            ApiUsabilityProbeResponse modelsProbe = default;
            yield return SendProbeCoroutine(
                BuildProbeRequest(runtime.ModelsEndpoint, "GET", null, config.Provider, config.ApiKey, CloudTimeoutSeconds),
                probe => modelsProbe = probe);
            bool modelsEndpointMissing = false;
            string modelsFallbackDetail = string.Empty;

            if (!modelsProbe.IsHttpSuccess)
            {
                if (!IsModelsEndpointMissingStatusCode(modelsProbe.HttpCode))
                {
                    onCompleted?.Invoke(BuildFailureFromProbe(
                        ApiUsabilityStep.ModelsProbe,
                        modelsProbe,
                        runtime.ModelsEndpoint,
                        startedAtUtc,
                        steps,
                        modelName,
                        true,
                        null));
                    yield break;
                }

                modelsEndpointMissing = true;
                modelsFallbackDetail = BuildMissingModelsFallbackDetail(modelsProbe.HttpCode);
                Log.Warning($"[RimChat] Models endpoint missing (HTTP {modelsProbe.HttpCode}), fallback to chat probe. endpoint={runtime.ModelsEndpoint}");
            }

            steps.Add(BuildStepSuccess(ApiUsabilityStep.ModelsProbe, runtime.ModelsEndpoint, startedAtUtc));
            NotifyProgress(onProgress, ApiUsabilityStep.ModelAvailability, 4, totalSteps);
            if (!modelsEndpointMissing)
            {
                List<string> cloudModels = ParseCloudModels(modelsProbe.ResponseBody, config.Provider);
                if (!ContainsModel(cloudModels, modelName))
                {
                    string detail = BuildMissingModelDetail(modelName, cloudModels);
                    onCompleted?.Invoke(BuildFailure(
                        ApiUsabilityStep.ModelAvailability,
                        ApiUsabilityErrorCode.MODEL_NOT_FOUND,
                        detail,
                        modelsProbe.HttpCode,
                        runtime.ModelsEndpoint,
                        startedAtUtc,
                        steps,
                        modelName,
                        true,
                        null,
                        modelsProbe.ResponseBody));
                    yield break;
                }
            }

            steps.Add(BuildStepSuccess(ApiUsabilityStep.ModelAvailability, runtime.ModelsEndpoint, startedAtUtc));
            NotifyProgress(onProgress, ApiUsabilityStep.ChatProbe, 5, totalSteps);
            string chatPayload = BuildOpenAiChatPayload(modelName);
            ApiUsabilityProbeResponse chatProbe = default;
            yield return SendProbeCoroutine(
                BuildProbeRequest(runtime.ChatEndpoint, "POST", chatPayload, config.Provider, config.ApiKey, CloudTimeoutSeconds),
                probe => chatProbe = probe);

            if (!chatProbe.IsHttpSuccess)
            {
                onCompleted?.Invoke(BuildFailureFromProbe(
                    ApiUsabilityStep.ChatProbe,
                    chatProbe,
                    runtime.ChatEndpoint,
                    startedAtUtc,
                    steps,
                    modelName,
                    true,
                    chatPayload));
                yield break;
            }

            steps.Add(BuildStepSuccess(ApiUsabilityStep.ChatProbe, runtime.ChatEndpoint, startedAtUtc));
            if (modelsEndpointMissing)
            {
                string responseModel = ExtractModelFromChatResponse(chatProbe.ResponseBody);
                if (!string.IsNullOrWhiteSpace(responseModel) && !ContainsModel(new List<string> { responseModel }, modelName))
                {
                    string detail = $"Model name mismatch: configured='{modelName}' but API returned='{responseModel}'. The /models endpoint is missing, and the chat response model name does not match.";
                    onCompleted?.Invoke(BuildFailure(
                        ApiUsabilityStep.ChatProbe,
                        ApiUsabilityErrorCode.MODEL_NOT_FOUND,
                        detail,
                        chatProbe.HttpCode,
                        runtime.ChatEndpoint,
                        startedAtUtc,
                        steps,
                        modelName,
                        true,
                        chatPayload,
                        chatProbe.ResponseBody));
                    yield break;
                }
            }

            NotifyProgress(onProgress, ApiUsabilityStep.ResponseContractValidation, 6, totalSteps);
            ContractValidationOutcome cloudContract = ValidateOpenAiChatContract(chatProbe.ResponseBody);
            ApiUsabilityProbeResponse cloudFinalProbe = chatProbe;
            bool cloudRetried = false;
            if (cloudContract.ShouldRetry)
            {
                cloudRetried = true;
                ApiUsabilityProbeResponse retryProbe = default;
                yield return SendProbeCoroutine(
                    BuildProbeRequest(runtime.ChatEndpoint, "POST", chatPayload, config.Provider, config.ApiKey, CloudTimeoutSeconds),
                    probe => retryProbe = probe);

                if (!retryProbe.IsHttpSuccess)
                {
                    onCompleted?.Invoke(BuildFailureFromProbe(
                        ApiUsabilityStep.ResponseContractValidation,
                        retryProbe,
                        runtime.ChatEndpoint,
                        startedAtUtc,
                        steps,
                        modelName,
                        true,
                        chatPayload));
                    yield break;
                }

                cloudFinalProbe = retryProbe;
                cloudContract = ValidateOpenAiChatContract(retryProbe.ResponseBody);
            }

            if (!cloudContract.IsAccepted)
            {
                onCompleted?.Invoke(BuildFailure(
                    ApiUsabilityStep.ResponseContractValidation,
                    ApiUsabilityErrorCode.RESPONSE_SCHEMA_INVALID,
                    cloudContract.Detail,
                    cloudFinalProbe.HttpCode,
                    runtime.ChatEndpoint,
                    startedAtUtc,
                    steps,
                    modelName,
                    true,
                    chatPayload,
                    cloudFinalProbe.ResponseBody));
                yield break;
            }

            steps.Add(BuildStepSuccess(ApiUsabilityStep.ResponseContractValidation, runtime.ChatEndpoint, startedAtUtc));
            string cloudSuccessDetail = BuildContractValidationSuccessDetail(cloudContract, cloudRetried);
            if (modelsEndpointMissing)
            {
                cloudSuccessDetail = AppendDiagnosticDetail(cloudSuccessDetail, modelsFallbackDetail);
            }
            onCompleted?.Invoke(BuildSuccess(
                ApiUsabilityStep.ResponseContractValidation,
                cloudFinalProbe.HttpCode,
                runtime.ChatEndpoint,
                startedAtUtc,
                steps,
                modelName,
                true,
                chatPayload,
                cloudFinalProbe.ResponseBody,
                cloudSuccessDetail));
        }

        /// <summary>
        /// Simplified diagnostic for Player2: no models endpoint, model selected server-side.
        /// Steps: ConfigValidation → ChatProbe → ResponseContractValidation
        /// </summary>
        private static IEnumerator RunPlayer2CloudDiagnosticCoroutine(
            ApiConfig config,
            Action<ApiUsabilityProgress> onProgress,
            Action<ApiUsabilityDiagnosticResult> onCompleted,
            DateTime startedAtUtc,
            List<ApiUsabilityStepResult> steps)
        {
            const int totalSteps = 3;
            string modelName = "Default";

            // Step 1: Config validation
            NotifyProgress(onProgress, ApiUsabilityStep.ConfigValidation, 1, totalSteps);
            if (config == null || !config.IsEnabled)
            {
                onCompleted?.Invoke(BuildFailure(
                    ApiUsabilityStep.ConfigValidation,
                    ApiUsabilityErrorCode.UNKNOWN,
                    "Player2 config is null or disabled.",
                    0, string.Empty, startedAtUtc, steps, modelName, true, null, string.Empty));
                yield break;
            }
            if (string.IsNullOrWhiteSpace(config.ApiKey))
            {
                onCompleted?.Invoke(BuildFailure(
                    ApiUsabilityStep.ConfigValidation,
                    ApiUsabilityErrorCode.AUTH_INVALID,
                    "RimChat_EnterApiKey".Translate(),
                    0, string.Empty, startedAtUtc, steps, modelName, true, null, string.Empty));
                yield break;
            }
            steps.Add(BuildStepSuccess(ApiUsabilityStep.ConfigValidation, string.Empty, startedAtUtc));

            // Step 2: Chat probe
            NotifyProgress(onProgress, ApiUsabilityStep.ChatProbe, 2, totalSteps);
            string chatEndpoint = config.GetEffectiveEndpoint();
            string chatPayload = BuildPlayer2ChatPayload();
            ApiUsabilityProbeResponse chatProbe = default;
            yield return SendProbeCoroutine(
                BuildProbeRequest(chatEndpoint, "POST", chatPayload, config.Provider, config.ApiKey ?? string.Empty, CloudTimeoutSeconds),
                probe => chatProbe = probe);

            if (!chatProbe.IsHttpSuccess)
            {
                onCompleted?.Invoke(BuildFailureFromProbe(
                    ApiUsabilityStep.ChatProbe, chatProbe, chatEndpoint,
                    startedAtUtc, steps, modelName, true, chatPayload));
                yield break;
            }
            steps.Add(BuildStepSuccess(ApiUsabilityStep.ChatProbe, chatEndpoint, startedAtUtc));

            // Step 3: Response contract validation
            NotifyProgress(onProgress, ApiUsabilityStep.ResponseContractValidation, 3, totalSteps);
            ContractValidationOutcome contract = ValidateOpenAiChatContract(chatProbe.ResponseBody);
            ApiUsabilityProbeResponse finalProbe = chatProbe;
            bool retried = false;
            if (contract.ShouldRetry)
            {
                retried = true;
                ApiUsabilityProbeResponse retryProbe = default;
                yield return SendProbeCoroutine(
                    BuildProbeRequest(chatEndpoint, "POST", chatPayload, config.Provider, config.ApiKey ?? string.Empty, CloudTimeoutSeconds),
                    probe => retryProbe = probe);

                if (!retryProbe.IsHttpSuccess)
                {
                    onCompleted?.Invoke(BuildFailureFromProbe(
                        ApiUsabilityStep.ResponseContractValidation, retryProbe, chatEndpoint,
                        startedAtUtc, steps, modelName, true, chatPayload));
                    yield break;
                }
                finalProbe = retryProbe;
                contract = ValidateOpenAiChatContract(retryProbe.ResponseBody);
            }

            if (!contract.IsAccepted)
            {
                onCompleted?.Invoke(BuildFailure(
                    ApiUsabilityStep.ResponseContractValidation,
                    ApiUsabilityErrorCode.RESPONSE_SCHEMA_INVALID,
                    contract.Detail,
                    finalProbe.HttpCode, chatEndpoint, startedAtUtc, steps, modelName, true, chatPayload, finalProbe.ResponseBody));
                yield break;
            }

            steps.Add(BuildStepSuccess(ApiUsabilityStep.ResponseContractValidation, chatEndpoint, startedAtUtc));
            string successDetail = BuildContractValidationSuccessDetail(contract, retried);
            onCompleted?.Invoke(BuildSuccess(
                ApiUsabilityStep.ResponseContractValidation,
                finalProbe.HttpCode, chatEndpoint, startedAtUtc, steps, modelName, true, chatPayload, finalProbe.ResponseBody, successDetail));
        }

        /// <summary>
        /// Simplified diagnostic for Player2 local app: no model listing, no Ollama probe.
        /// Steps: ConfigValidation → ChatProbe → ResponseContractValidation
        /// </summary>
        private static IEnumerator RunPlayer2LocalDiagnosticCoroutine(
            LocalModelConfig config,
            Action<ApiUsabilityProgress> onProgress,
            Action<ApiUsabilityDiagnosticResult> onCompleted,
            DateTime startedAtUtc,
            List<ApiUsabilityStepResult> steps)
        {
            const int totalSteps = 3;
            string modelName = "Default";
            string normalizedBaseUrl = ApiConfig.NormalizeUrl(config.GetNormalizedBaseUrl());

            // Step 1: Config validation
            NotifyProgress(onProgress, ApiUsabilityStep.ConfigValidation, 1, totalSteps);
            if (string.IsNullOrWhiteSpace(normalizedBaseUrl))
            {
                onCompleted?.Invoke(BuildFailure(
                    ApiUsabilityStep.ConfigValidation,
                    ApiUsabilityErrorCode.UNKNOWN,
                    "Player2 local config requires a base URL.",
                    0, string.Empty, startedAtUtc, steps, modelName, false, null, string.Empty));
                yield break;
            }
            steps.Add(BuildStepSuccess(ApiUsabilityStep.ConfigValidation, normalizedBaseUrl, startedAtUtc));

            // Step 2: Chat probe
            NotifyProgress(onProgress, ApiUsabilityStep.ChatProbe, 2, totalSteps);
            string chatEndpoint = normalizedBaseUrl.TrimEnd('/') + "/v1/chat/completions";
            string chatPayload = BuildPlayer2ChatPayload();
            ApiUsabilityProbeResponse chatProbe = default;
            yield return SendProbeCoroutine(
                BuildProbeRequest(chatEndpoint, "POST", chatPayload, AIProvider.Player2, string.Empty, CloudTimeoutSeconds),
                probe => chatProbe = probe);

            if (!chatProbe.IsHttpSuccess)
            {
                onCompleted?.Invoke(BuildFailureFromProbe(
                    ApiUsabilityStep.ChatProbe, chatProbe, chatEndpoint,
                    startedAtUtc, steps, modelName, false, chatPayload));
                yield break;
            }
            steps.Add(BuildStepSuccess(ApiUsabilityStep.ChatProbe, chatEndpoint, startedAtUtc));

            // Step 3: Response contract validation
            NotifyProgress(onProgress, ApiUsabilityStep.ResponseContractValidation, 3, totalSteps);
            ContractValidationOutcome contract = ValidateOpenAiChatContract(chatProbe.ResponseBody);
            ApiUsabilityProbeResponse finalProbe = chatProbe;
            bool retried = false;
            if (contract.ShouldRetry)
            {
                retried = true;
                ApiUsabilityProbeResponse retryProbe = default;
                yield return SendProbeCoroutine(
                    BuildProbeRequest(chatEndpoint, "POST", chatPayload, AIProvider.Player2, string.Empty, CloudTimeoutSeconds),
                    probe => retryProbe = probe);

                if (!retryProbe.IsHttpSuccess)
                {
                    onCompleted?.Invoke(BuildFailureFromProbe(
                        ApiUsabilityStep.ResponseContractValidation, retryProbe, chatEndpoint,
                        startedAtUtc, steps, modelName, false, chatPayload));
                    yield break;
                }
                finalProbe = retryProbe;
                contract = ValidateOpenAiChatContract(retryProbe.ResponseBody);
            }

            if (!contract.IsAccepted)
            {
                onCompleted?.Invoke(BuildFailure(
                    ApiUsabilityStep.ResponseContractValidation,
                    ApiUsabilityErrorCode.RESPONSE_SCHEMA_INVALID,
                    contract.Detail,
                    finalProbe.HttpCode, chatEndpoint, startedAtUtc, steps, modelName, false, chatPayload, finalProbe.ResponseBody));
                yield break;
            }

            steps.Add(BuildStepSuccess(ApiUsabilityStep.ResponseContractValidation, chatEndpoint, startedAtUtc));
            string successDetail = BuildContractValidationSuccessDetail(contract, retried);
            onCompleted?.Invoke(BuildSuccess(
                ApiUsabilityStep.ResponseContractValidation,
                finalProbe.HttpCode, chatEndpoint, startedAtUtc, steps, modelName, false, chatPayload, finalProbe.ResponseBody, successDetail));
        }

        internal static IEnumerator RunLocalDiagnosticCoroutine(
            LocalModelConfig config,
            Action<ApiUsabilityProgress> onProgress,
            Action<ApiUsabilityDiagnosticResult> onCompleted)
        {
            DateTime startedAtUtc = DateTime.UtcNow;
            var steps = new List<ApiUsabilityStepResult>();

            // Player2 local: simplified diagnostic (no model listing, no Ollama probe)
            if (config != null && config.IsPlayer2Local())
            {
                yield return RunPlayer2LocalDiagnosticCoroutine(config, onProgress, onCompleted, startedAtUtc, steps);
                yield break;
            }

            const int totalSteps = 4;
            string modelName = config?.ModelName ?? string.Empty;
            string normalizedBaseUrl = ApiConfig.NormalizeUrl(config?.GetNormalizedBaseUrl() ?? string.Empty);

            ApiUsabilityDiagnosticResult validationFailure = ValidateLocalConfig(config, startedAtUtc, steps);
            if (validationFailure != null)
            {
                onCompleted?.Invoke(validationFailure);
                yield break;
            }

            NotifyProgress(onProgress, ApiUsabilityStep.ConfigValidation, 1, totalSteps);
            steps.Add(BuildStepSuccess(ApiUsabilityStep.ConfigValidation, normalizedBaseUrl, startedAtUtc));
            NotifyProgress(onProgress, ApiUsabilityStep.LocalServiceProbe, 2, totalSteps);

            ApiUsabilityLocalServiceProbe localProbe = default;
            yield return ProbeLocalServiceCoroutine(normalizedBaseUrl, probe => localProbe = probe);
            if (!localProbe.IsSuccess)
            {
                onCompleted?.Invoke(BuildFailureFromProbe(
                    ApiUsabilityStep.LocalServiceProbe,
                    localProbe.Response,
                    localProbe.EndpointUsed,
                    startedAtUtc,
                    steps,
                    modelName,
                    false,
                    null,
                    ApiUsabilityErrorCode.LOCAL_SERVICE_DOWN));
                yield break;
            }

            steps.Add(BuildStepSuccess(ApiUsabilityStep.LocalServiceProbe, localProbe.EndpointUsed, startedAtUtc));
            NotifyProgress(onProgress, ApiUsabilityStep.ChatProbe, 3, totalSteps);
            string chatEndpoint = BuildLocalChatEndpoint(normalizedBaseUrl, localProbe.ServiceType);
            string chatPayload = BuildLocalChatPayload(modelName, localProbe.ServiceType);
            ApiUsabilityProbeResponse chatProbe = default;
            yield return SendProbeCoroutine(
                BuildProbeRequest(chatEndpoint, "POST", chatPayload, AIProvider.None, string.Empty, LocalTimeoutSeconds),
                probe => chatProbe = probe);

            if (!chatProbe.IsHttpSuccess)
            {
                onCompleted?.Invoke(BuildFailureFromProbe(
                    ApiUsabilityStep.ChatProbe,
                    chatProbe,
                    chatEndpoint,
                    startedAtUtc,
                    steps,
                    modelName,
                    false,
                    chatPayload));
                yield break;
            }

            steps.Add(BuildStepSuccess(ApiUsabilityStep.ChatProbe, chatEndpoint, startedAtUtc));
            NotifyProgress(onProgress, ApiUsabilityStep.ResponseContractValidation, 4, totalSteps);
            ContractValidationOutcome localContract = ValidateLocalChatContract(localProbe.ServiceType, chatProbe.ResponseBody);
            ApiUsabilityProbeResponse localFinalProbe = chatProbe;
            bool localRetried = false;
            if (localContract.ShouldRetry)
            {
                localRetried = true;
                ApiUsabilityProbeResponse retryProbe = default;
                yield return SendProbeCoroutine(
                    BuildProbeRequest(chatEndpoint, "POST", chatPayload, AIProvider.None, string.Empty, LocalTimeoutSeconds),
                    probe => retryProbe = probe);

                if (!retryProbe.IsHttpSuccess)
                {
                    onCompleted?.Invoke(BuildFailureFromProbe(
                        ApiUsabilityStep.ResponseContractValidation,
                        retryProbe,
                        chatEndpoint,
                        startedAtUtc,
                        steps,
                        modelName,
                        false,
                        chatPayload));
                    yield break;
                }

                localFinalProbe = retryProbe;
                localContract = ValidateLocalChatContract(localProbe.ServiceType, retryProbe.ResponseBody);
            }

            if (!localContract.IsAccepted)
            {
                onCompleted?.Invoke(BuildFailure(
                    ApiUsabilityStep.ResponseContractValidation,
                    ApiUsabilityErrorCode.RESPONSE_SCHEMA_INVALID,
                    localContract.Detail,
                    localFinalProbe.HttpCode,
                    chatEndpoint,
                    startedAtUtc,
                    steps,
                    modelName,
                    false,
                    chatPayload,
                    localFinalProbe.ResponseBody));
                yield break;
            }

            steps.Add(BuildStepSuccess(ApiUsabilityStep.ResponseContractValidation, chatEndpoint, startedAtUtc));
            string localSuccessDetail = BuildContractValidationSuccessDetail(localContract, localRetried);
            onCompleted?.Invoke(BuildSuccess(
                ApiUsabilityStep.ResponseContractValidation,
                localFinalProbe.HttpCode,
                chatEndpoint,
                startedAtUtc,
                steps,
                modelName,
                false,
                chatPayload,
                localFinalProbe.ResponseBody,
                localSuccessDetail));
        }

        internal static string GetStepLabelKey(ApiUsabilityStep step)
        {
            return step switch
            {
                ApiUsabilityStep.ConfigValidation => "RimChat_UsabilityStep_ConfigValidation",
                ApiUsabilityStep.RuntimeEndpointResolution => "RimChat_UsabilityStep_RuntimeEndpointResolution",
                ApiUsabilityStep.ModelsProbe => "RimChat_UsabilityStep_ModelsProbe",
                ApiUsabilityStep.ModelAvailability => "RimChat_UsabilityStep_ModelAvailability",
                ApiUsabilityStep.LocalServiceProbe => "RimChat_UsabilityStep_LocalServiceProbe",
                ApiUsabilityStep.ChatProbe => "RimChat_UsabilityStep_ChatProbe",
                _ => "RimChat_UsabilityStep_ResponseContractValidation"
            };
        }

        internal static string GetErrorTitleKey(ApiUsabilityErrorCode code)
        {
            return code switch
            {
                ApiUsabilityErrorCode.AUTH_INVALID => "RimChat_UsabilityError_AUTH_INVALID",
                ApiUsabilityErrorCode.ENDPOINT_NOT_FOUND => "RimChat_UsabilityError_ENDPOINT_NOT_FOUND",
                ApiUsabilityErrorCode.MODEL_NOT_FOUND => "RimChat_UsabilityError_MODEL_NOT_FOUND",
                ApiUsabilityErrorCode.TIMEOUT => "RimChat_UsabilityError_TIMEOUT",
                ApiUsabilityErrorCode.RATE_LIMIT => "RimChat_UsabilityError_RATE_LIMIT",
                ApiUsabilityErrorCode.TLS_OR_CERT => "RimChat_UsabilityError_TLS_OR_CERT",
                ApiUsabilityErrorCode.DNS_OR_NETWORK => "RimChat_UsabilityError_DNS_OR_NETWORK",
                ApiUsabilityErrorCode.RESPONSE_SCHEMA_INVALID => "RimChat_UsabilityError_RESPONSE_SCHEMA_INVALID",
                ApiUsabilityErrorCode.LOCAL_SERVICE_DOWN => "RimChat_UsabilityError_LOCAL_SERVICE_DOWN",
                _ => "RimChat_UsabilityError_UNKNOWN"
            };
        }

        private static ApiUsabilityDiagnosticResult ValidateCloudConfig(
            ApiConfig config,
            DateTime startedAtUtc,
            List<ApiUsabilityStepResult> steps,
            string modelName)
        {
            if (config == null)
            {
                return BuildFailure(
                    ApiUsabilityStep.ConfigValidation,
                    ApiUsabilityErrorCode.UNKNOWN,
                    "Cloud config is null.",
                    0,
                    string.Empty,
                    startedAtUtc,
                    steps,
                    modelName,
                    true,
                    null,
                    string.Empty);
            }

            if (string.IsNullOrWhiteSpace(config.ApiKey))
            {
                return BuildFailure(
                    ApiUsabilityStep.ConfigValidation,
                    ApiUsabilityErrorCode.UNKNOWN,
                    "RimChat_EnterApiKey".Translate(),
                    0,
                    string.Empty,
                    startedAtUtc,
                    steps,
                    modelName,
                    true,
                    null,
                    string.Empty);
            }

            if (string.IsNullOrWhiteSpace(modelName))
            {
                return BuildFailure(
                    ApiUsabilityStep.ConfigValidation,
                    ApiUsabilityErrorCode.UNKNOWN,
                    "RimChat_ErrorEmptyModel".Translate(),
                    0,
                    string.Empty,
                    startedAtUtc,
                    steps,
                    modelName,
                    true,
                    null,
                    string.Empty);
            }

            return null;
        }

        private static ApiUsabilityDiagnosticResult ValidateLocalConfig(
            LocalModelConfig config,
            DateTime startedAtUtc,
            List<ApiUsabilityStepResult> steps)
        {
            string baseUrl = config?.GetNormalizedBaseUrl() ?? string.Empty;
            // Player2 local does not require a model name
            bool missingRequired = string.IsNullOrWhiteSpace(baseUrl) ||
                (!config.IsPlayer2Local() && string.IsNullOrWhiteSpace(config?.ModelName));
            if (missingRequired)
            {
                return BuildFailure(
                    ApiUsabilityStep.ConfigValidation,
                    ApiUsabilityErrorCode.UNKNOWN,
                    "Local config requires base URL and model name.",
                    0,
                    baseUrl,
                    startedAtUtc,
                    steps,
                    config?.ModelName ?? string.Empty,
                    false,
                    null,
                    string.Empty);
            }

            return null;
        }

        private static ApiUsabilityCloudRuntime ResolveCloudRuntime(ApiConfig config)
        {
            string modelsEndpoint;
            string chatEndpoint;
            string details = string.Empty;
            if (config.Provider == AIProvider.Custom && config.TryResolveCustomRuntimeEndpoints(out CustomUrlRuntimeResolution resolved))
            {
                modelsEndpoint = ApiConfig.NormalizeUrl(resolved.ModelsEndpoint);
                chatEndpoint = ApiConfig.NormalizeUrl(resolved.ChatEndpoint);
                if (resolved.HasSuspiciousBasePath)
                {
                    details = "Custom URL keeps a suspicious base path.";
                }
            }
            else
            {
                modelsEndpoint = ResolveCloudModelsEndpoint(config);
                chatEndpoint = ResolveCloudChatEndpoint(config);
            }

            return new ApiUsabilityCloudRuntime
            {
                ModelsEndpoint = modelsEndpoint,
                ChatEndpoint = chatEndpoint,
                Details = details
            };
        }

        private static string ResolveCloudModelsEndpoint(ApiConfig config)
        {
            if (config == null)
            {
                return string.Empty;
            }

            if (config.Provider == AIProvider.DeepSeek)
            {
                return config.Provider.GetListModelsUrl();
            }

            string baseUrl = ApiConfig.NormalizeUrl(config.BaseUrl);
            return string.IsNullOrWhiteSpace(baseUrl)
                ? config.Provider.GetListModelsUrl()
                : ApiConfig.ToModelsEndpoint(baseUrl);
        }

        private static string ResolveCloudChatEndpoint(ApiConfig config)
        {
            if (config == null)
            {
                return string.Empty;
            }

            if (config.Provider == AIProvider.DeepSeek)
            {
                return config.Provider.GetEndpointUrl();
            }

            string baseUrl = ApiConfig.NormalizeUrl(config.BaseUrl);
            return string.IsNullOrWhiteSpace(baseUrl)
                ? config.GetEffectiveEndpoint()
                : ApiConfig.EnsureChatCompletionsEndpoint(baseUrl);
        }

        private static IEnumerator ProbeLocalServiceCoroutine(
            string normalizedBaseUrl,
            Action<ApiUsabilityLocalServiceProbe> onCompleted)
        {
            ApiUsabilityProbeResponse ollamaProbe = default;
            string ollamaEndpoint = JoinUrl(normalizedBaseUrl, LocalOllamaProbePath);
            yield return SendProbeCoroutine(
                BuildProbeRequest(ollamaEndpoint, "GET", null, AIProvider.None, string.Empty, LocalTimeoutSeconds),
                probe => ollamaProbe = probe);

            if (ollamaProbe.IsHttpSuccess)
            {
                onCompleted?.Invoke(new ApiUsabilityLocalServiceProbe
                {
                    IsSuccess = true,
                    EndpointUsed = ollamaEndpoint,
                    ServiceType = ApiUsabilityLocalServiceType.Ollama,
                    Response = ollamaProbe
                });
                yield break;
            }

            ApiUsabilityProbeResponse openAiProbe = default;
            string openAiEndpoint = JoinUrl(normalizedBaseUrl, LocalOpenAiModelsPath);
            yield return SendProbeCoroutine(
                BuildProbeRequest(openAiEndpoint, "GET", null, AIProvider.None, string.Empty, LocalTimeoutSeconds),
                probe => openAiProbe = probe);

            if (openAiProbe.IsHttpSuccess)
            {
                onCompleted?.Invoke(new ApiUsabilityLocalServiceProbe
                {
                    IsSuccess = true,
                    EndpointUsed = openAiEndpoint,
                    ServiceType = ApiUsabilityLocalServiceType.OpenAiCompatible,
                    Response = openAiProbe
                });
                yield break;
            }

            if (IsModelsEndpointMissingStatusCode(openAiProbe.HttpCode))
            {
                Log.Warning($"[RimChat] Local OpenAI-compatible models endpoint missing (HTTP {openAiProbe.HttpCode}), fallback to chat probe. endpoint={openAiEndpoint}");
                onCompleted?.Invoke(new ApiUsabilityLocalServiceProbe
                {
                    IsSuccess = true,
                    EndpointUsed = openAiEndpoint,
                    ServiceType = ApiUsabilityLocalServiceType.OpenAiCompatible,
                    Response = openAiProbe
                });
                yield break;
            }

            ApiUsabilityProbeResponse failed = openAiProbe.HttpCode > 0 ? openAiProbe : ollamaProbe;
            onCompleted?.Invoke(new ApiUsabilityLocalServiceProbe
            {
                IsSuccess = false,
                EndpointUsed = failed.HttpCode > 0 ? openAiEndpoint : ollamaEndpoint,
                ServiceType = ApiUsabilityLocalServiceType.Unknown,
                Response = failed
            });
        }

        private static ApiUsabilityProbeRequest BuildProbeRequest(
            string url,
            string method,
            string body,
            AIProvider provider,
            string apiKey,
            int timeoutSeconds)
        {
            return new ApiUsabilityProbeRequest
            {
                Url = ApiConfig.NormalizeUrl(url),
                Method = string.IsNullOrWhiteSpace(method) ? "GET" : method,
                Body = body ?? string.Empty,
                Provider = provider,
                ApiKey = (apiKey ?? string.Empty).Trim(),
                TimeoutSeconds = Mathf.Clamp(timeoutSeconds, 3, 30)
            };
        }

        private static IEnumerator SendProbeCoroutine(
            ApiUsabilityProbeRequest request,
            Action<ApiUsabilityProbeResponse> onCompleted)
        {
            if (string.IsNullOrWhiteSpace(request.Url))
            {
                onCompleted?.Invoke(new ApiUsabilityProbeResponse
                {
                    HttpCode = 0,
                    Error = "Empty URL.",
                    ResponseBody = string.Empty
                });
                yield break;
            }

            using (var web = new UnityWebRequest(request.Url, request.Method))
            {
                web.downloadHandler = new DownloadHandlerBuffer();
                web.timeout = request.TimeoutSeconds;
                ApplyProbeAuthHeader(web, request.Provider, request.ApiKey, request.Url);
                if (!string.IsNullOrWhiteSpace(request.Body))
                {
                    byte[] payload = Encoding.UTF8.GetBytes(request.Body);
                    web.uploadHandler = new UploadHandlerRaw(payload);
                    web.SetRequestHeader("Content-Type", "application/json");
                }

                yield return web.SendWebRequest();
                onCompleted?.Invoke(new ApiUsabilityProbeResponse
                {
                    HttpCode = web.responseCode,
                    Error = web.error ?? string.Empty,
                    ResponseBody = web.downloadHandler?.text ?? string.Empty
                });
            }
        }

        private static void ApplyProbeAuthHeader(UnityWebRequest request, AIProvider provider, string apiKey, string requestUrl)
        {
            // Player2 requires both Bearer token and game-key header
            if (provider == AIProvider.Player2)
            {
                string trimmedKey = apiKey?.Trim();
                if (!string.IsNullOrWhiteSpace(trimmedKey))
                {
                    request.SetRequestHeader("Authorization", $"Bearer {trimmedKey}");
                }
                var extraHeaders = provider.GetExtraHeaders();
                if (extraHeaders != null)
                {
                    foreach (var header in extraHeaders)
                    {
                        request.SetRequestHeader(header.Key, header.Value);
                    }
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return;
            }

            if (provider == AIProvider.Google)
            {
                if (IsGoogleOpenAiCompatibleUrl(requestUrl))
                {
                    request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                }
                else
                {
                    request.SetRequestHeader("x-goog-api-key", apiKey);
                }

                return;
            }

            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        }

        private static bool IsGoogleOpenAiCompatibleUrl(string requestUrl)
        {
            if (string.IsNullOrWhiteSpace(requestUrl))
            {
                return false;
            }

            return requestUrl.IndexOf("/openai/", StringComparison.OrdinalIgnoreCase) >= 0
                || requestUrl.IndexOf("/chat/completions", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<string> ParseCloudModels(string responseBody, AIProvider provider)
        {
            if (provider == AIProvider.Google)
            {
                List<string> googleModels = ExtractQuotedValues(responseBody, "\"name\"");
                return googleModels
                    .Select(NormalizeGoogleModelName)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            List<string> openAiModels = ExtractQuotedValues(responseBody, "\"id\"");
            if (openAiModels.Count == 0)
            {
                openAiModels = ExtractQuotedValues(responseBody, "\"model\"");
            }

            return openAiModels
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> ParseLocalModels(ApiUsabilityLocalServiceType serviceType, string responseBody)
        {
            List<string> source = serviceType == ApiUsabilityLocalServiceType.Ollama
                ? ExtractQuotedValues(responseBody, "\"name\"")
                : ExtractQuotedValues(responseBody, "\"id\"");

            return source
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool ContainsModel(List<string> models, string targetModel)
        {
            if (models == null || models.Count == 0 || string.IsNullOrWhiteSpace(targetModel))
            {
                return false;
            }

            string normalized = targetModel.Trim();
            string prefix = BuildModelTagPrefix(normalized);
            return models.Any(model =>
                string.Equals(model, normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(model, prefix, StringComparison.OrdinalIgnoreCase) ||
                model.StartsWith(normalized + ":", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsModelsEndpointMissingStatusCode(long httpCode)
        {
            return httpCode == 404 || httpCode == 405 || httpCode == 501;
        }

        private static string BuildMissingModelsFallbackDetail(long httpCode)
        {
            string codeText = httpCode > 0 ? httpCode.ToString() : "unknown";
            return $"models_endpoint_missing_http={codeText}; fallback_to_chat_probe=true";
        }

        private static string BuildOpenAiChatPayload(string modelName)
        {
            string escapedModel = EscapeJsonString(modelName);
            return "{"
                + $"\"model\":\"{escapedModel}\","
                + "\"messages\":[{\"role\":\"user\",\"content\":\"ping\"}],"
                + "\"max_tokens\":8,"
                + "\"temperature\":0"
                + "}";
        }

        /// <summary>
        /// Player2 does not accept a model field; model is selected server-side.
        /// </summary>
        private static string BuildPlayer2ChatPayload()
        {
            return "{"
                + "\"messages\":[{\"role\":\"user\",\"content\":\"ping\"}],"
                + "\"max_tokens\":8,"
                + "\"temperature\":0"
                + "}";
        }

        private static string BuildLocalChatEndpoint(string baseUrl, ApiUsabilityLocalServiceType serviceType)
        {
            return serviceType == ApiUsabilityLocalServiceType.Ollama
                ? JoinUrl(baseUrl, LocalOllamaGeneratePath)
                : JoinUrl(baseUrl, LocalOpenAiChatPath);
        }

        private static string BuildLocalChatPayload(string modelName, ApiUsabilityLocalServiceType serviceType)
        {
            string escapedModel = EscapeJsonString(modelName);
            if (serviceType == ApiUsabilityLocalServiceType.Ollama)
            {
                return "{"
                    + $"\"model\":\"{escapedModel}\","
                    + "\"prompt\":\"ping\","
                    + "\"stream\":false"
                    + "}";
            }

            return BuildOpenAiChatPayload(modelName);
        }

        private static ContractValidationOutcome ValidateOpenAiChatContract(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return ContractValidationOutcome.Fail("Chat response body is empty.");
            }

            if (!ContainsJsonField(responseBody, "\"choices\""))
            {
                return ContractValidationOutcome.Fail("Missing choices field.");
            }

            List<string> contents = ExtractQuotedValues(responseBody, "\"content\"");
            if (contents.Count > 0 && contents.Any(value => !string.IsNullOrWhiteSpace(value)))
            {
                return ContractValidationOutcome.Pass();
            }

            bool hasFinishReason = ContainsJsonField(responseBody, "\"finish_reason\"");
            bool hasUsageSignal = ContainsJsonField(responseBody, "\"usage\"")
                || ContainsJsonField(responseBody, "\"prompt_tokens\"")
                || ContainsJsonField(responseBody, "\"completion_tokens\"")
                || ContainsJsonField(responseBody, "\"total_tokens\"");
            if (hasFinishReason || hasUsageSignal)
            {
                return ContractValidationOutcome.Warning("Missing assistant content; accepted by finish_reason/usage signal.");
            }

            return ContractValidationOutcome.RetryableFail("Missing assistant content without finish_reason/usage signal.");
        }

        private static string ExtractModelFromChatResponse(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return null;
            }

            List<string> models = ExtractQuotedValues(responseBody, "\"model\"");
            return models.FirstOrDefault(model => !string.IsNullOrWhiteSpace(model));
        }

        private static ContractValidationOutcome ValidateLocalChatContract(ApiUsabilityLocalServiceType serviceType, string responseBody)
        {
            if (serviceType != ApiUsabilityLocalServiceType.Ollama)
            {
                return ValidateOpenAiChatContract(responseBody);
            }

            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return ContractValidationOutcome.Fail("Ollama response body is empty.");
            }

            List<string> values = ExtractQuotedValues(responseBody, "\"response\"");
            if (values.Count > 0 && values.Any(value => !string.IsNullOrWhiteSpace(value)))
            {
                return ContractValidationOutcome.Pass();
            }

            bool hasDoneSignal = ContainsJsonField(responseBody, "\"done\"")
                || ContainsJsonField(responseBody, "\"eval_count\"")
                || ContainsJsonField(responseBody, "\"prompt_eval_count\"");
            if (hasDoneSignal)
            {
                return ContractValidationOutcome.Warning("Missing response text; accepted by ollama completion signal.");
            }

            return ContractValidationOutcome.RetryableFail("Missing response field without ollama completion signal.");
        }

        private static string BuildContractValidationSuccessDetail(ContractValidationOutcome outcome, bool retried)
        {
            if (outcome.IsWarning)
            {
                string retryText = retried ? "true" : "false";
                return $"contract_warning={outcome.Detail}; retry_probe={retryText}";
            }

            if (retried)
            {
                return "retry_probe=true";
            }

            return string.Empty;
        }

        private static string AppendDiagnosticDetail(string primary, string extra)
        {
            bool hasPrimary = !string.IsNullOrWhiteSpace(primary);
            bool hasExtra = !string.IsNullOrWhiteSpace(extra);
            if (!hasPrimary)
            {
                return hasExtra ? extra.Trim() : string.Empty;
            }

            if (!hasExtra)
            {
                return primary.Trim();
            }

            return $"{primary.Trim()}; {extra.Trim()}";
        }

        private static bool ContainsJsonField(string responseBody, string fieldToken)
        {
            return !string.IsNullOrWhiteSpace(responseBody)
                && !string.IsNullOrWhiteSpace(fieldToken)
                && responseBody.IndexOf(fieldToken, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private struct ContractValidationOutcome
        {
            public bool IsAccepted;
            public bool ShouldRetry;
            public bool IsWarning;
            public string Detail;

            public static ContractValidationOutcome Pass()
            {
                return new ContractValidationOutcome
                {
                    IsAccepted = true,
                    ShouldRetry = false,
                    IsWarning = false,
                    Detail = string.Empty
                };
            }

            public static ContractValidationOutcome Warning(string detail)
            {
                return new ContractValidationOutcome
                {
                    IsAccepted = true,
                    ShouldRetry = false,
                    IsWarning = true,
                    Detail = detail ?? string.Empty
                };
            }

            public static ContractValidationOutcome RetryableFail(string detail)
            {
                return new ContractValidationOutcome
                {
                    IsAccepted = false,
                    ShouldRetry = true,
                    IsWarning = false,
                    Detail = detail ?? string.Empty
                };
            }

            public static ContractValidationOutcome Fail(string detail)
            {
                return new ContractValidationOutcome
                {
                    IsAccepted = false,
                    ShouldRetry = false,
                    IsWarning = false,
                    Detail = detail ?? string.Empty
                };
            }
        }

    }

    internal enum ApiUsabilityStep
    {
        ConfigValidation = 0,
        RuntimeEndpointResolution = 1,
        ModelsProbe = 2,
        ModelAvailability = 3,
        LocalServiceProbe = 4,
        ChatProbe = 5,
        ResponseContractValidation = 6
    }

    internal enum ApiUsabilityErrorCode
    {
        NONE = 0,
        AUTH_INVALID = 1,
        ENDPOINT_NOT_FOUND = 2,
        MODEL_NOT_FOUND = 3,
        TIMEOUT = 4,
        RATE_LIMIT = 5,
        TLS_OR_CERT = 6,
        DNS_OR_NETWORK = 7,
        RESPONSE_SCHEMA_INVALID = 8,
        LOCAL_SERVICE_DOWN = 9,
        UNKNOWN = 10
    }

    internal sealed class ApiUsabilityProgress
    {
        public ApiUsabilityStep Step { get; set; }
        public int Current { get; set; }
        public int Total { get; set; }
    }

    internal sealed class ApiUsabilityStepResult
    {
        public ApiUsabilityStep Step { get; set; }
        public bool Success { get; set; }
        public ApiUsabilityErrorCode ErrorCode { get; set; }
        public string TechDetail { get; set; }
        public long HttpCode { get; set; }
        public string EndpointUsed { get; set; }
        public long ElapsedMs { get; set; }
    }

    internal sealed class ApiUsabilityDiagnosticResult
    {
        public bool IsSuccess { get; set; }
        public ApiUsabilityStep Step { get; set; }
        public ApiUsabilityErrorCode ErrorCode { get; set; }
        public string TechDetail { get; set; }
        public long HttpCode { get; set; }
        public string EndpointUsed { get; set; }
        public long ElapsedMs { get; set; }
        public List<ApiUsabilityStepResult> Steps { get; set; } = new List<ApiUsabilityStepResult>();
        public List<string> PlayerHintKeys { get; set; } = new List<string>();
        public List<ApiDiagnosticSuggestion> Suggestions { get; set; } = new List<ApiDiagnosticSuggestion>();
        public string DebugRequestText { get; set; }
        public string DebugResponseText { get; set; }
        public string ModelName { get; set; }
        public bool IsCloud { get; set; }
        public AIProvider Provider { get; set; } = AIProvider.None;
    }
}
