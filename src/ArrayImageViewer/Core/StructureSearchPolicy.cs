using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace ArrayImageViewer.Core
{
    internal sealed class StructureSearchLimitException : InvalidOperationException
    {
        public StructureSearchLimitException(string message) : base(message) { }
    }

    internal enum StructureSearchExpansion
    {
        None,
        ObjectMembers,
        InterestedContainerElements
    }

    // The debugger may make every expression evaluation a cross-process COM
    // call. Keep the decision about which nodes are worth expanding pure and
    // testable, so capture visits the object graph rather than RAW storage.
    internal static class StructureSearchPolicy
    {
        // Count every debugger entry, including null/presentation entries.
        // Checking before MoveNext avoids requesting one more expensive child.
        public static IEnumerable<object> EnumerateBoundedChildren(IEnumerable children, int maximumChildren, Action checkBudget)
        {
            var enumerator = children.GetEnumerator();
            try
            {
                for (int index = 0; index < maximumChildren; index++)
                {
                    checkBudget();
                    if (!enumerator.MoveNext()) yield break;
                    checkBudget();
                    yield return enumerator.Current;
                }
                throw new StructureSearchLimitException("Child enumeration limit reached.");
            }
            finally
            {
                var disposable = enumerator as IDisposable;
                if (disposable != null) disposable.Dispose();
            }
        }

        public static string FindInterestedTypeName(string type, IList<string> interestedTypeNames)
        {
            if (String.IsNullOrWhiteSpace(type) || interestedTypeNames == null)
            {
                return null;
            }

            var normalized = NormalizeType(type);
            if (normalized.Length == 0 || IsArrayType(normalized))
            {
                return null;
            }

            for (var index = 0; index < interestedTypeNames.Count; index++)
            {
                var candidate = NormalizeType(interestedTypeNames[index]);
                if (candidate.Length == 0)
                {
                    continue;
                }

                if (String.Equals(normalized, candidate, StringComparison.Ordinal) ||
                    normalized.EndsWith("::" + candidate, StringComparison.Ordinal))
                {
                    return interestedTypeNames[index];
                }
            }

            return null;
        }

        public static StructureSearchExpansion GetExpansion(string type, IList<string> interestedTypeNames)
        {
            var normalized = NormalizeType(type);
            if (normalized.Length == 0 || FindInterestedTypeName(normalized, interestedTypeNames) != null ||
                IsPrimitiveOrPrimitivePointer(normalized))
            {
                return StructureSearchExpansion.None;
            }

            if (IsInterestedStandardContainer(normalized, interestedTypeNames))
            {
                return StructureSearchExpansion.InterestedContainerElements;
            }

            // Do not walk vector implementation fields, allocators, strings,
            // or arbitrary standard-library storage. Only containers whose
            // element type is a registered template are opened above.
            if (normalized.IndexOf("std::", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return StructureSearchExpansion.None;
            }

            return StructureSearchExpansion.ObjectMembers;
        }

        public static bool IsContainerElementName(string childName)
        {
            return !String.IsNullOrWhiteSpace(childName) && childName.TrimStart().StartsWith("[", StringComparison.Ordinal);
        }

        public static string CreateCacheKey(string rootExpression, IList<string> interestedTypeNames)
        {
            var names = new List<string>();
            if (interestedTypeNames != null)
            {
                for (var index = 0; index < interestedTypeNames.Count; index++)
                {
                    var name = NormalizeType(interestedTypeNames[index]);
                    if (name.Length > 0)
                    {
                        names.Add(name);
                    }
                }
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);

            var builder = new StringBuilder((rootExpression ?? String.Empty).Trim());
            for (var index = 0; index < names.Count; index++)
            {
                builder.Append('\n').Append(names[index]);
            }
            return builder.ToString();
        }

        private static bool IsInterestedStandardContainer(string normalizedType, IList<string> interestedTypeNames)
        {
            if (!normalizedType.StartsWith("std::vector<", StringComparison.OrdinalIgnoreCase) &&
                !normalizedType.StartsWith("std::array<", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var elementType = GetFirstTemplateArgument(normalizedType);
            return FindInterestedTypeName(elementType, interestedTypeNames) != null;
        }

        private static string GetFirstTemplateArgument(string type)
        {
            var open = type.IndexOf('<');
            if (open < 0 || open == type.Length - 1)
            {
                return String.Empty;
            }

            var depth = 0;
            for (var index = open + 1; index < type.Length; index++)
            {
                var character = type[index];
                if (character == '<')
                {
                    depth++;
                }
                else if (character == '>')
                {
                    if (depth == 0)
                    {
                        return type.Substring(open + 1, index - open - 1).Trim();
                    }
                    depth--;
                }
                else if (character == ',' && depth == 0)
                {
                    return type.Substring(open + 1, index - open - 1).Trim();
                }
            }

            return String.Empty;
        }

        private static bool IsPrimitiveOrPrimitivePointer(string type)
        {
            var arrayStart = type.IndexOf('[');
            if (arrayStart >= 0) type = type.Substring(0, arrayStart);
            var normalized = type.Trim().TrimEnd('*', '&', ' ');
            if (normalized == "uint8" || normalized == "uint16" || normalized == "uint32" || normalized == "uint64" ||
                normalized == "int8" || normalized == "int16" || normalized == "int32" || normalized == "int64" ||
                normalized == "uint8_t" || normalized == "uint16_t" || normalized == "uint32_t" || normalized == "uint64_t" ||
                normalized == "int8_t" || normalized == "int16_t" || normalized == "int32_t" || normalized == "int64_t") return true;
            return normalized == "char" || normalized == "signed char" || normalized == "unsigned char" ||
                normalized == "short" || normalized == "unsigned short" || normalized == "int" || normalized == "unsigned int" ||
                normalized == "long" || normalized == "unsigned long" || normalized == "long long" || normalized == "unsigned long long" ||
                normalized == "float" || normalized == "double" || normalized == "bool" || normalized == "void" ||
                normalized.IndexOf("enum ", StringComparison.Ordinal) == 0;
        }

        private static bool IsArrayType(string type)
        {
            return type.IndexOf('[') >= 0;
        }

        private static string NormalizeType(string type)
        {
            var normalized = (type ?? String.Empty).Trim();
            normalized = normalized.Replace("class ", String.Empty).Replace("struct ", String.Empty).Replace("const ", String.Empty).Trim();
            normalized = normalized.TrimEnd('*', '&', ' ');
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s*([<>,:*&\[\]])\s*", "$1");
            return normalized;
        }
    }
}
