using System;

namespace ArrayImageViewer.Core
{
    // Keeps debugger-expression composition independent from the VS shell so
    // it can be tested without a live debuggee. Access may be a member path
    // (D), an explicit suffix (->D), or a full {root}-based expression.
    internal static class StructureTemplateExpressions
    {
        public static string Compose(string root, string access)
        {
            var normalizedRoot = (root ?? String.Empty).Trim();
            var normalizedAccess = (access ?? String.Empty).Trim();
            if (normalizedRoot.Length == 0)
            {
                throw new ArgumentException("Enter a root expression for the structure template.");
            }
            if (normalizedAccess.Length == 0)
            {
                throw new ArgumentException("Enter a member access expression for the structure template.");
            }

            if (normalizedAccess.IndexOf("{root}", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return normalizedAccess.Replace("{root}", "(" + normalizedRoot + ")");
            }
            if (normalizedAccess.StartsWith("->", StringComparison.Ordinal) || normalizedAccess.StartsWith(".", StringComparison.Ordinal) ||
                normalizedAccess.StartsWith("[", StringComparison.Ordinal))
            {
                return "(" + normalizedRoot + ")" + normalizedAccess;
            }
            if (normalizedAccess.StartsWith(normalizedRoot, StringComparison.Ordinal))
            {
                return normalizedAccess;
            }

            return "(" + normalizedRoot + ")->" + normalizedAccess;
        }
    }
}
