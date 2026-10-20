using System;
using System.Collections.Generic;
using UnityEngine;

namespace StratusFieldLink.Model
{
    /// <summary>
    /// Wire models. Field names are lower-camel on purpose: JsonUtility matches
    /// field names verbatim and the API serialises camelCase. Do not rename these
    /// without changing the server contract.
    /// </summary>
    [Serializable]
    public class SketchNodeDto
    {
        public float x;
        public float y;
        public float z;

        public SketchNodeDto()
        {
        }

        public SketchNodeDto(Vector3 point)
        {
            x = point.x;
            y = point.y;
            z = point.z;
        }

        public Vector3 ToVector3()
        {
            return new Vector3(x, y, z);
        }
    }

    [Serializable]
    public class FieldSketchDto
    {
        public string id;
        public string packageId;
        public string crewId;
        public string discipline = "Piping";
        public float diameterInches = 4f;
        public string material = "Carbon Steel";
        public List<SketchNodeDto> nodes = new List<SketchNodeDto>();
        public string capturedAtUtc;
        public float clientLengthFeet;
        public string clientVersion;

        /// <summary>Retries already spent from the offline queue on server errors. Client-only bookkeeping.</summary>
        public int retryCount;

        /// <summary>Non-empty when the queue must not auto-send this sketch (409, 4xx, retries exhausted).</summary>
        public string holdReason;

        public static FieldSketchDto NewDraft(string packageId, string crewId)
        {
            return new FieldSketchDto
            {
                id = "SK-" + Guid.NewGuid().ToString("N").Substring(0, 10).ToUpperInvariant(),
                packageId = packageId,
                crewId = crewId,
                capturedAtUtc = DateTime.UtcNow.ToString("o"),
                clientVersion = Config.FieldLinkConfig.ClientVersion,
                nodes = new List<SketchNodeDto>()
            };
        }
    }

    [Serializable]
    public class ApiFailureDto
    {
        public string field;
        public string message;
    }

    /// <summary>Shape the API returns on a 4xx: a summary plus, for a 400, per-field failures.</summary>
    [Serializable]
    public class ApiErrorDto
    {
        public string error;
        public List<ApiFailureDto> failures;

        /// <summary>One human-readable line for the foreman. Empty when the body says nothing useful.</summary>
        public static string Describe(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return string.Empty;
            }

            ApiErrorDto parsed;
            try
            {
                parsed = JsonUtility.FromJson<ApiErrorDto>(body);
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }

            if (parsed == null)
            {
                return string.Empty;
            }

            if (parsed.failures != null && parsed.failures.Count > 0)
            {
                var messages = new List<string>();
                foreach (var failure in parsed.failures)
                {
                    messages.Add(failure.message);
                }

                return string.Join(" ", messages);
            }

            return parsed.error ?? string.Empty;
        }
    }
}
