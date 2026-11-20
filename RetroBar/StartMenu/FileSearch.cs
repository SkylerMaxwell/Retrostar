using ManagedShell.Common.Logging;
using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RetroBar.StartMenu
{
    /// <summary>
    /// Searches files and folders through the local Windows Search index, the same index that
    /// Vista's Start menu and Windows 11's search use for local results. Nothing leaves the PC.
    /// </summary>
    internal static class FileSearch
    {
        private const string ConnectionString = "Provider=Search.CollatorDSO;Extended Properties='Application=Windows';";

        public static async Task<List<StartMenuEntry>> SearchAsync(string text, int maxResults, CancellationToken token)
        {
            string term = Sanitize(text);

            if (term.Length == 0)
            {
                return new List<StartMenuEntry>();
            }

            List<(string path, string name)> hits = await Task.Run(() => Query(term, maxResults, token), token).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();

            // Icons come from the shell, which needs an STA thread
            return await StaTask.Run(() =>
            {
                var results = new List<StartMenuEntry>();

                foreach (var hit in hits)
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }

                    results.Add(new StartMenuEntry
                    {
                        Kind = StartMenuEntryKind.File,
                        Target = hit.path,
                        Name = hit.name,
                        Icon = ShellIcons.FromPath(hit.path, ShellIconSize.Small)
                    });
                }

                return results;
            }).ConfigureAwait(false);
        }

        private static List<(string path, string name)> Query(string term, int maxResults, CancellationToken token)
        {
            var hits = new List<(string path, string name)>();

            try
            {
                using var connection = new OleDbConnection(ConnectionString);
                connection.Open();

                // Programs are listed separately, so shortcuts are excluded here.
                string sql =
                    $"SELECT TOP {maxResults * 2} System.ItemPathDisplay, System.ItemNameDisplay " +
                    "FROM SystemIndex " +
                    $"WHERE SCOPE='file:' AND System.ItemNameDisplay LIKE '%{term}%' " +
                    "ORDER BY System.DateModified DESC";

                using var command = new OleDbCommand(sql, connection);
                using OleDbDataReader reader = command.ExecuteReader();

                while (reader != null && reader.Read() && hits.Count < maxResults)
                {
                    token.ThrowIfCancellationRequested();

                    string path = reader.IsDBNull(0) ? null : reader.GetValue(0) as string;
                    string name = reader.IsDBNull(1) ? null : reader.GetValue(1) as string;

                    if (string.IsNullOrEmpty(path) || string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    hits.Add((path, string.IsNullOrEmpty(name) ? Path.GetFileName(path) : name));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                // The Windows Search service may be disabled; program results still work without it
                ShellLogger.Debug($"FileSearch: Windows Search query failed: {e.Message}");
            }

            return hits;
        }

        /// <summary>
        /// Removes characters with special meaning in Windows Search SQL so typed text is matched literally.
        /// </summary>
        private static string Sanitize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var chars = new List<char>(text.Length);

            foreach (char c in text.Trim())
            {
                if (c == '\'' || c == '%' || c == '_' || c == '[' || c == ']' || c == '"' || char.IsControl(c))
                {
                    continue;
                }

                chars.Add(c);
            }

            return new string(chars.ToArray());
        }
    }
}
