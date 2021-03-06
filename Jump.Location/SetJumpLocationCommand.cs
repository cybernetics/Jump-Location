﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation;

namespace Jump.Location
{
    [Cmdlet(VerbsCommon.Set, "JumpLocation", DefaultParameterSetName = "Query")]
    public class SetJumpLocationCommand : PSCmdlet
    {
        private static readonly CommandController Controller = CommandController.DefaultInstance;

        public static IEnumerable<string> GetTabExpansion(string line, string lastWord)
        {
            // line is something like "j term1 term2 temr3". 
            // Skip cmdlet name and call match for the rest.
            string[] searchTerms = line.Split().Skip(1).ToArray();
            return Controller.GetMatchesForSearchTerm(searchTerms).Select(GetResultPath);
        }

        private static string GetResultPath(IRecord record)
        {
            var candidate = record.Path;
            if (candidate.Contains(" "))
                return string.Format("\"{0}\"", candidate);
            return candidate;
        }

        [Parameter(ParameterSetName = "Query", ValueFromRemainingArguments = true)]
        public string[] Query { get; set; }

        [Parameter(ParameterSetName = "Initialize", 
            HelpMessage = "Initialize Jump-Location by starting to listen to directory changes.")]
        public SwitchParameter Initialize { get; set; }

        public static void UpdateTime(string location)
        {
            Controller.UpdateLocation(location);
        }

        protected override void ProcessRecord()
        {
            // This lets us do just `Jump-Location -Initialize` to initialize everything in the profile script
            if (Initialize)
            {
                InvokeCommand.InvokeScript(@"
                    Register-EngineEvent -SourceIdentifier PowerShell.OnIdle -Action {
                        [Jump.Location.SetJumpLocationCommand]::UpdateTime($($(Get-Item -Path $(Get-Location))).PSPath)
                    }
                ");
                return;
            }

            if (Query == null) { Query = new string[] {}; }

            // "j -" is an alias for popd. 
            if (Query.Length == 1 && Query[0] == "-")
            {
                InvokeCommand.InvokeScript("Pop-Location");
                return;
            }

            // If last term is absolute path it's probably because of autocomplition
            // we can safely process it here.
            if (Query.Any() && Path.IsPathRooted(Query.Last()))
            {
                if (!Directory.Exists(Query.Last()))
                {
                    throw new LocationNotFoundException(Query.Last());
                }
                ChangeDirectory(Query.Last());
                return;
            }

            IEnumerable<IRecord> orderedMatches = Controller.GetMatchesForSearchTerm(Query);
            if (orderedMatches == null) throw new LocationNotFoundException(String.Join(" ", Query));
            bool destinationFound = false;
            foreach (IRecord match in orderedMatches)
            {
                if (match.Provider == @"Microsoft.PowerShell.Core\FileSystem" && !Directory.Exists(match.Path))
                {
                    WriteWarning(String.Format("Skipping {0}: directory not found. You can remove obsolete directories from database with command 'jumpstat -cleanup'.", match.Path));
                    continue;
                }
                ChangeDirectory(match.Path);
                destinationFound = true;
                break;
            }

            if (!destinationFound)
            {
                // ask PowerShell to try to resolve the path (handling for things like "~")
                var resolvedPath = GetUnresolvedProviderPathFromPSPath(Query.Last());

                if (!Directory.Exists(resolvedPath))
                {
                    throw new LocationNotFoundException(String.Join(" ", Query));
                }
                ChangeDirectory(Query.Last());
            }
        }

        private void ChangeDirectory(string fullPath)
        {
            InvokeCommand.InvokeScript(string.Format("Push-Location '{0}'", fullPath.Trim()));
        }
    }
}
