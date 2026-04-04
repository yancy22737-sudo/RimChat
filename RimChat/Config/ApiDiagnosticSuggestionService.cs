using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.AI;

namespace RimChat.Config
{
    internal sealed class ApiDiagnosticSuggestion
    {
        public int ProbabilityPercent;
        public string CauseKey;
        public string SolutionKey;
    }

    internal static class ApiDiagnosticSuggestionService
    {
        private const int DefaultSuggestionCount = 4;
        private const int ProbabilitySum = 100;

        internal static List<ApiDiagnosticSuggestion> GenerateSuggestions(
            ApiUsabilityErrorCode errorCode,
            long httpCode,
            string errorMessage,
            AIProvider provider,
            bool isCloud,
            string endpoint)
        {
            switch (errorCode)
            {
                case ApiUsabilityErrorCode.AUTH_INVALID:
                    return GenerateAuthInvalidSuggestions(httpCode, provider, isCloud);
                case ApiUsabilityErrorCode.ENDPOINT_NOT_FOUND:
                    return GenerateEndpointNotFoundSuggestions(httpCode, provider, isCloud, endpoint);
                case ApiUsabilityErrorCode.MODEL_NOT_FOUND:
                    return GenerateModelNotFoundSuggestions(provider, isCloud);
                case ApiUsabilityErrorCode.TIMEOUT:
                    return GenerateTimeoutSuggestions(httpCode, provider, isCloud);
                case ApiUsabilityErrorCode.RATE_LIMIT:
                    return GenerateRateLimitSuggestions(provider, isCloud);
                case ApiUsabilityErrorCode.TLS_OR_CERT:
                    return GenerateTlsCertSuggestions(provider, isCloud);
                case ApiUsabilityErrorCode.DNS_OR_NETWORK:
                    return GenerateDnsNetworkSuggestions(provider, isCloud);
                case ApiUsabilityErrorCode.RESPONSE_SCHEMA_INVALID:
                    return GenerateResponseSchemaInvalidSuggestions(provider, isCloud);
                case ApiUsabilityErrorCode.LOCAL_SERVICE_DOWN:
                    return GenerateLocalServiceDownSuggestions(provider);
                default:
                    return GenerateUnknownSuggestions(httpCode, isCloud);
            }
        }

        private static List<ApiDiagnosticSuggestion> GenerateAuthInvalidSuggestions(long httpCode, AIProvider provider, bool isCloud)
        {
            if (httpCode == 401)
            {
                return new List<ApiDiagnosticSuggestion>
                {
                    new ApiDiagnosticSuggestion { ProbabilityPercent = 40, CauseKey = "RimChat_Suggestion_AuthInvalid_401_WrongKey", SolutionKey = "RimChat_Suggestion_AuthInvalid_401_WrongKey_Solution" },
                    new ApiDiagnosticSuggestion { ProbabilityPercent = 25, CauseKey = "RimChat_Suggestion_AuthInvalid_401_Expired", SolutionKey = "RimChat_Suggestion_AuthInvalid_401_Expired_Solution" },
                    new ApiDiagnosticSuggestion { ProbabilityPercent = 20, CauseKey = "RimChat_Suggestion_AuthInvalid_401_Format", SolutionKey = "RimChat_Suggestion_AuthInvalid_401_Format_Solution" },
                    new ApiDiagnosticSuggestion { ProbabilityPercent = 15, CauseKey = "RimChat_Suggestion_AuthInvalid_401_Permission", SolutionKey = "RimChat_Suggestion_AuthInvalid_401_Permission_Solution" }
                };
            }

            return new List<ApiDiagnosticSuggestion>
            {
                new ApiDiagnosticSuggestion { ProbabilityPercent = 45, CauseKey = "RimChat_Suggestion_AuthInvalid_403_Forbidden", SolutionKey = "RimChat_Suggestion_AuthInvalid_403_Forbidden_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 25, CauseKey = "RimChat_Suggestion_AuthInvalid_403_Quota", SolutionKey = "RimChat_Suggestion_AuthInvalid_403_Quota_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 18, CauseKey = "RimChat_Suggestion_AuthInvalid_403_Region", SolutionKey = "RimChat_Suggestion_AuthInvalid_403_Region_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 12, CauseKey = "RimChat_Suggestion_AuthInvalid_403_Other", SolutionKey = "RimChat_Suggestion_AuthInvalid_403_Other_Solution" }
            };
        }

        private static List<ApiDiagnosticSuggestion> GenerateEndpointNotFoundSuggestions(long httpCode, AIProvider provider, bool isCloud, string endpoint)
        {
            if (isCloud)
            {
                return new List<ApiDiagnosticSuggestion>
                {
                    new ApiDiagnosticSuggestion { ProbabilityPercent = 40, CauseKey = "RimChat_Suggestion_EndpointNotFound_CustomBaseUrl", SolutionKey = "RimChat_Suggestion_EndpointNotFound_CustomBaseUrl_Solution" },
                    new ApiDiagnosticSuggestion { ProbabilityPercent = 28, CauseKey = "RimChat_Suggestion_EndpointNotFound_ProviderUrl", SolutionKey = "RimChat_Suggestion_EndpointNotFound_ProviderUrl_Solution" },
                    new ApiDiagnosticSuggestion { ProbabilityPercent = 20, CauseKey = "RimChat_Suggestion_EndpointNotFound_Path", SolutionKey = "RimChat_Suggestion_EndpointNotFound_Path_Solution" },
                    new ApiDiagnosticSuggestion { ProbabilityPercent = 12, CauseKey = "RimChat_Suggestion_EndpointNotFound_ProviderDown", SolutionKey = "RimChat_Suggestion_EndpointNotFound_ProviderDown_Solution" }
                };
            }

            return new List<ApiDiagnosticSuggestion>
            {
                new ApiDiagnosticSuggestion { ProbabilityPercent = 45, CauseKey = "RimChat_Suggestion_EndpointNotFound_LocalNotRunning", SolutionKey = "RimChat_Suggestion_EndpointNotFound_LocalNotRunning_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 25, CauseKey = "RimChat_Suggestion_EndpointNotFound_LocalPort", SolutionKey = "RimChat_Suggestion_EndpointNotFound_LocalPort_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 18, CauseKey = "RimChat_Suggestion_EndpointNotFound_LocalFirewall", SolutionKey = "RimChat_Suggestion_EndpointNotFound_LocalFirewall_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 12, CauseKey = "RimChat_Suggestion_EndpointNotFound_LocalNetwork", SolutionKey = "RimChat_Suggestion_EndpointNotFound_LocalNetwork_Solution" }
            };
        }

        private static List<ApiDiagnosticSuggestion> GenerateModelNotFoundSuggestions(AIProvider provider, bool isCloud)
        {
            return new List<ApiDiagnosticSuggestion>
            {
                new ApiDiagnosticSuggestion { ProbabilityPercent = 42, CauseKey = "RimChat_Suggestion_ModelNotFound_WrongName", SolutionKey = "RimChat_Suggestion_ModelNotFound_WrongName_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 28, CauseKey = "RimChat_Suggestion_ModelNotFound_NotDeployed", SolutionKey = "RimChat_Suggestion_ModelNotFound_NotDeployed_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 18, CauseKey = "RimChat_Suggestion_ModelNotFound_Quota", SolutionKey = "RimChat_Suggestion_ModelNotFound_Quota_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 12, CauseKey = "RimChat_Suggestion_ModelNotFound_Region", SolutionKey = "RimChat_Suggestion_ModelNotFound_Region_Solution" }
            };
        }

        private static List<ApiDiagnosticSuggestion> GenerateTimeoutSuggestions(long httpCode, AIProvider provider, bool isCloud)
        {
            if (isCloud)
            {
                return new List<ApiDiagnosticSuggestion>
                {
                    new ApiDiagnosticSuggestion { ProbabilityPercent = 38, CauseKey = "RimChat_Suggestion_Timeout_Network", SolutionKey = "RimChat_Suggestion_Timeout_Network_Solution" },
                    new ApiDiagnosticSuggestion { ProbabilityPercent = 28, CauseKey = "RimChat_Suggestion_Timeout_ServerBusy", SolutionKey = "RimChat_Suggestion_Timeout_ServerBusy_Solution" },
                    new ApiDiagnosticSuggestion { ProbabilityPercent = 20, CauseKey = "RimChat_Suggestion_Timeout_Firewall", SolutionKey = "RimChat_Suggestion_Timeout_Firewall_Solution" },
                    new ApiDiagnosticSuggestion { ProbabilityPercent = 14, CauseKey = "RimChat_Suggestion_Timeout_Proxy", SolutionKey = "RimChat_Suggestion_Timeout_Proxy_Solution" }
                };
            }

            return new List<ApiDiagnosticSuggestion>
            {
                new ApiDiagnosticSuggestion { ProbabilityPercent = 40, CauseKey = "RimChat_Suggestion_Timeout_LocalBusy", SolutionKey = "RimChat_Suggestion_Timeout_LocalBusy_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 28, CauseKey = "RimChat_Suggestion_Timeout_LocalModel", SolutionKey = "RimChat_Suggestion_Timeout_LocalModel_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 20, CauseKey = "RimChat_Suggestion_Timeout_LocalMemory", SolutionKey = "RimChat_Suggestion_Timeout_LocalMemory_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 12, CauseKey = "RimChat_Suggestion_Timeout_LocalGPU", SolutionKey = "RimChat_Suggestion_Timeout_LocalGPU_Solution" }
            };
        }

        private static List<ApiDiagnosticSuggestion> GenerateRateLimitSuggestions(AIProvider provider, bool isCloud)
        {
            return new List<ApiDiagnosticSuggestion>
            {
                new ApiDiagnosticSuggestion { ProbabilityPercent = 45, CauseKey = "RimChat_Suggestion_RateLimit_TooMany", SolutionKey = "RimChat_Suggestion_RateLimit_TooMany_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 25, CauseKey = "RimChat_Suggestion_RateLimit_Plan", SolutionKey = "RimChat_Suggestion_RateLimit_Plan_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 18, CauseKey = "RimChat_Suggestion_RateLimit_Burst", SolutionKey = "RimChat_Suggestion_RateLimit_Burst_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 12, CauseKey = "RimChat_Suggestion_RateLimit_Retry", SolutionKey = "RimChat_Suggestion_RateLimit_Retry_Solution" }
            };
        }

        private static List<ApiDiagnosticSuggestion> GenerateTlsCertSuggestions(AIProvider provider, bool isCloud)
        {
            return new List<ApiDiagnosticSuggestion>
            {
                new ApiDiagnosticSuggestion { ProbabilityPercent = 40, CauseKey = "RimChat_Suggestion_TlsCert_Expired", SolutionKey = "RimChat_Suggestion_TlsCert_Expired_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 28, CauseKey = "RimChat_Suggestion_TlsCert_SelfSigned", SolutionKey = "RimChat_Suggestion_TlsCert_SelfSigned_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 20, CauseKey = "RimChat_Suggestion_TlsCert_Mismatch", SolutionKey = "RimChat_Suggestion_TlsCert_Mismatch_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 12, CauseKey = "RimChat_Suggestion_TlsCert_CA", SolutionKey = "RimChat_Suggestion_TlsCert_CA_Solution" }
            };
        }

        private static List<ApiDiagnosticSuggestion> GenerateDnsNetworkSuggestions(AIProvider provider, bool isCloud)
        {
            if (isCloud)
            {
                return new List<ApiDiagnosticSuggestion>
                {
                    new ApiDiagnosticSuggestion { ProbabilityPercent = 42, CauseKey = "RimChat_Suggestion_DnsNetwork_DNS", SolutionKey = "RimChat_Suggestion_DnsNetwork_DNS_Solution" },
                    new ApiDiagnosticSuggestion { ProbabilityPercent = 28, CauseKey = "RimChat_Suggestion_DnsNetwork_Firewall", SolutionKey = "RimChat_Suggestion_DnsNetwork_Firewall_Solution" },
                    new ApiDiagnosticSuggestion { ProbabilityPercent = 18, CauseKey = "RimChat_Suggestion_DnsNetwork_ISP", SolutionKey = "RimChat_Suggestion_DnsNetwork_ISP_Solution" },
                    new ApiDiagnosticSuggestion { ProbabilityPercent = 12, CauseKey = "RimChat_Suggestion_DnsNetwork_VPN", SolutionKey = "RimChat_Suggestion_DnsNetwork_VPN_Solution" }
                };
            }

            return new List<ApiDiagnosticSuggestion>
            {
                new ApiDiagnosticSuggestion { ProbabilityPercent = 45, CauseKey = "RimChat_Suggestion_DnsNetwork_LocalNotRunning", SolutionKey = "RimChat_Suggestion_DnsNetwork_LocalNotRunning_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 25, CauseKey = "RimChat_Suggestion_DnsNetwork_LocalAddress", SolutionKey = "RimChat_Suggestion_DnsNetwork_LocalAddress_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 18, CauseKey = "RimChat_Suggestion_DnsNetwork_LocalFirewall", SolutionKey = "RimChat_Suggestion_DnsNetwork_LocalFirewall_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 12, CauseKey = "RimChat_Suggestion_DnsNetwork_LocalNetwork", SolutionKey = "RimChat_Suggestion_DnsNetwork_LocalNetwork_Solution" }
            };
        }

        private static List<ApiDiagnosticSuggestion> GenerateResponseSchemaInvalidSuggestions(AIProvider provider, bool isCloud)
        {
            return new List<ApiDiagnosticSuggestion>
            {
                new ApiDiagnosticSuggestion { ProbabilityPercent = 38, CauseKey = "RimChat_Suggestion_ResponseSchema_ModelChange", SolutionKey = "RimChat_Suggestion_ResponseSchema_ModelChange_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 28, CauseKey = "RimChat_Suggestion_ResponseSchema_ProviderIssue", SolutionKey = "RimChat_Suggestion_ResponseSchema_ProviderIssue_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 20, CauseKey = "RimChat_Suggestion_ResponseSchema_Cache", SolutionKey = "RimChat_Suggestion_ResponseSchema_Cache_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 14, CauseKey = "RimChat_Suggestion_ResponseSchema_Network", SolutionKey = "RimChat_Suggestion_ResponseSchema_Network_Solution" }
            };
        }

        private static List<ApiDiagnosticSuggestion> GenerateLocalServiceDownSuggestions(AIProvider provider)
        {
            return new List<ApiDiagnosticSuggestion>
            {
                new ApiDiagnosticSuggestion { ProbabilityPercent = 45, CauseKey = "RimChat_Suggestion_LocalServiceDown_NotStarted", SolutionKey = "RimChat_Suggestion_LocalServiceDown_NotStarted_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 25, CauseKey = "RimChat_Suggestion_LocalServiceDown_WrongPort", SolutionKey = "RimChat_Suggestion_LocalServiceDown_WrongPort_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 18, CauseKey = "RimChat_Suggestion_LocalServiceDown_ModelNotLoaded", SolutionKey = "RimChat_Suggestion_LocalServiceDown_ModelNotLoaded_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 12, CauseKey = "RimChat_Suggestion_LocalServiceDown_Crashed", SolutionKey = "RimChat_Suggestion_LocalServiceDown_Crashed_Solution" }
            };
        }

        private static List<ApiDiagnosticSuggestion> GenerateUnknownSuggestions(long httpCode, bool isCloud)
        {
            if (httpCode > 0)
            {
                return new List<ApiDiagnosticSuggestion>
                {
                    new ApiDiagnosticSuggestion { ProbabilityPercent = 40, CauseKey = "RimChat_Suggestion_Unknown_HttpError", SolutionKey = "RimChat_Suggestion_Unknown_HttpError_Solution" },
                    new ApiDiagnosticSuggestion { ProbabilityPercent = 28, CauseKey = "RimChat_Suggestion_Unknown_ServerError", SolutionKey = "RimChat_Suggestion_Unknown_ServerError_Solution" },
                    new ApiDiagnosticSuggestion { ProbabilityPercent = 20, CauseKey = "RimChat_Suggestion_Unknown_Network", SolutionKey = "RimChat_Suggestion_Unknown_Network_Solution" },
                    new ApiDiagnosticSuggestion { ProbabilityPercent = 12, CauseKey = "RimChat_Suggestion_Unknown_Other", SolutionKey = "RimChat_Suggestion_Unknown_Other_Solution" }
                };
            }

            return new List<ApiDiagnosticSuggestion>
            {
                new ApiDiagnosticSuggestion { ProbabilityPercent = 42, CauseKey = "RimChat_Suggestion_Unknown_NoResponse", SolutionKey = "RimChat_Suggestion_Unknown_NoResponse_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 28, CauseKey = "RimChat_Suggestion_Unknown_ConnectionFailed", SolutionKey = "RimChat_Suggestion_Unknown_ConnectionFailed_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 18, CauseKey = "RimChat_Suggestion_Unknown_Timeout", SolutionKey = "RimChat_Suggestion_Unknown_Timeout_Solution" },
                new ApiDiagnosticSuggestion { ProbabilityPercent = 12, CauseKey = "RimChat_Suggestion_Unknown_Firewall", SolutionKey = "RimChat_Suggestion_Unknown_Firewall_Solution" }
            };
        }
    }
}