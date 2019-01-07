﻿using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace bashWizardShared
{
    public partial class ScriptData
    {
        /// <summary>
        ///     the parameters we support that add built in functionality
        /// </summary>
        public enum BashWizardParameter { LoggingSupport, InputFile, CreateVerifyDelete };

        /// <summary>
        ///     given a BashWizardParameter turn the functionality on or off
        /// </summary>
        /// <param name="paramName">a BashWizard enum value of the property to set </param>
        /// /// <param name="set">turn the feature on or off </param>
        /// <returns>true if the API was able to do the action specified, otherwise false </returns>
        public (bool retVal, string msg) SetBuiltInParameter(BashWizardParameter paramName, bool set)
        {
            bool ret = false;
            string msg = "";
            switch (paramName)
            {
                case BashWizardParameter.LoggingSupport:
                    SetCreateLogDirectory(set);
                    ret = true;
                    break;
                case BashWizardParameter.InputFile:
                    SetAcceptsInputFile(set);
                    ret = true;
                    break;
                case BashWizardParameter.CreateVerifyDelete:
                    (ret, msg) = SetCreateVerifyDelete(set);
                    break;
                default:
                    break;
            }
            //
            //  you don't call ToBash() because modifying the Parameters collection will update the bash script
            //  

            return (ret, msg);
        }

        /// <summary>
        ///     Converts the parameters to a bash script.  
        /// </summary>
        /// <remarks>
        ///     The overall implementation strategy is to put as much as possible into "templates",
        ///     stored in resource files.  we then replace strings in the templates with strings we generate based on the parameters.  there are
        ///     three "phases": 1) loop through the parameters and build each string we need 2) fix up the strings we built - e.g. remove end of 
        ///     line characters and 3) use StringBuilder.Replace() to put the strings into the right place in the bash file.
        /// </remarks>
        /// <returns>true on success, false on failure</returns>
        public bool ToBash()
        {
            if (_doNotGenerateBashScript)
            {
                return false;
            }

            if (this.Parameters.Count == 0)
            {
                return false;

            }


            if (!ValidateParameters())
            {
                this.BashScript = ValidationErrors;
                return false;
            }

            
            string nl = "\n";

            StringBuilder sbBashScript = new StringBuilder(EmbeddedResource.GetResourceFile(Assembly.GetExecutingAssembly(), "bashTemplate.sh"));
            StringBuilder logTemplate = new StringBuilder(EmbeddedResource.GetResourceFile(Assembly.GetExecutingAssembly(), "logTemplate.sh"));
            StringBuilder parseInputTemplate = new StringBuilder(EmbeddedResource.GetResourceFile(Assembly.GetExecutingAssembly(), "parseInputTemplate.sh"));
            StringBuilder requiredVariablesTemplate = new StringBuilder(EmbeddedResource.GetResourceFile(Assembly.GetExecutingAssembly(), "requiredVariablesTemplate.sh"));
            StringBuilder verifyCreateDeleteTemplate = new StringBuilder(EmbeddedResource.GetResourceFile(Assembly.GetExecutingAssembly(), "verifyCreateDeleteTemplate.sh"));
            StringBuilder endLogTemplate = new StringBuilder(EmbeddedResource.GetResourceFile(Assembly.GetExecutingAssembly(), "endLogTemplate.sh"));
            StringBuilder usageLine = new StringBuilder($"{Tabs(1)}echo \"{this.Description}\"\n{Tabs(1)}echo \"\"\n{Tabs(1)}echo \"Usage: $0 ");
            StringBuilder usageInfo = new StringBuilder($"{Tabs(1)}echo \"\"\n");
            StringBuilder echoInput = new StringBuilder($"\"{ScriptName}:\"{nl}");
            StringBuilder shortOptions = new StringBuilder("");
            StringBuilder longOptions = new StringBuilder("");
            StringBuilder inputCase = new StringBuilder("");
            StringBuilder inputDeclarations = new StringBuilder("");
            StringBuilder parseInputFile = new StringBuilder("");
            StringBuilder requiredFilesIf = new StringBuilder("");
            StringBuilder loggingSupport = new StringBuilder("");
            int longestLongParameter = GetLongestLongParameter() + 4;
            //
            //   phase 1: loop through the parameters and build our strings
            foreach (ParameterItem param in Parameters)
            {
                //
                //  first usage line
                string required = (param.RequiredParameter) ? "Required" : "Optional";
                usageLine.Append($"-{param.ShortParameter}|--{param.LongParameter} ");
                usageInfo.Append($"{Tabs(1)}echo \" -{param.ShortParameter} | --{param.LongParameter.PadRight(longestLongParameter)}{Tabs(1)}{required}{Tabs(1)}{param.Description}\"{nl}");

                //
                // the  echoInput function
                echoInput.Append($"{Tabs(1)}echo -n \"{Tabs(1)}{param.LongParameter.PadRight(longestLongParameter, '.')} \"{nl}");
                echoInput.Append($"{Tabs(1)}echoInfo \"${param.VariableName}\"{nl}");

                //
                //  OPTIONS, LONGOPTS
                string colon = (param.RequiresInputString) ? ":" : "";
                shortOptions.Append($"{param.ShortParameter}{colon}");
                longOptions.Append($"{param.LongParameter}{colon},");

                // input Case
                inputCase.Append($"{Tabs(2)}-{param.ShortParameter} | --{param.LongParameter})\n");
                inputCase.Append($"{Tabs(3)}{param.VariableName}={param.ValueIfSet}\n");
                inputCase.Append((param.RequiresInputString) ? $"{Tabs(3)}shift 2\n" : $"{Tabs(3)}shift 1\n");
                inputCase.Append($"{Tabs(3)};;\n");

                // declare variables
                inputDeclarations.Append($"declare {param.VariableName}={param.Default}\n");
                if (this.AcceptsInputFile && param.VariableName != "inputFile")
                {

                    // parse input file
                    parseInputFile.Append($"{Tabs(1)}{param.VariableName}=$(echo \"${{configSection}}\" | jq \'.[\"{param.LongParameter}\"]\' --raw-output)\n");

                }

                // if statement for the required files

                if (param.RequiredParameter)
                {
                    requiredFilesIf.Append($"[ -z \"${{{param.VariableName}}}\" ] || ");
                }


            }


            //
            //  phase 2 - fix up any of the string created above         

            usageLine.Append("\"");

            longOptions.Remove(longOptions.Length - 1, 1);
            inputCase.Remove(inputCase.Length - 1, 1);
            usageInfo.Remove(usageInfo.Length - 1, 1);

            if (requiredFilesIf.Length > 0)
            {
                requiredFilesIf.Remove(requiredFilesIf.Length - 4, 4); // removes the " || " at the end
                requiredVariablesTemplate.Replace("__REQUIRED_FILES_IF__", requiredFilesIf.ToString());
            }
            else
            {
                requiredVariablesTemplate.Clear();
            }

            if (this.LoggingSupport)
            {
                logTemplate.Replace("__LOG_FILE_NAME__", this.ScriptName + ".log");

            }
            else
            {
                logTemplate.Clear();
            }

            //
            //  phase 3 - replace the strings in the templates
            sbBashScript.Replace("__USAGE_LINE__", usageLine.ToString());
            sbBashScript.Replace("__USAGE__", usageInfo.ToString());
            sbBashScript.Replace("__ECHO__", echoInput.ToString());
            sbBashScript.Replace("__SHORT_OPTIONS__", shortOptions.ToString());
            sbBashScript.Replace("__LONG_OPTIONS__", longOptions.ToString());
            sbBashScript.Replace("__INPUT_CASE__", inputCase.ToString());
            sbBashScript.Replace("__INPUT_DECLARATION__", inputDeclarations.ToString());

            string inputOverridesRequired = (this.AcceptsInputFile) ? "echoWarning \"Parameters can be passed in the command line or in the input file.  The command line overrides the setting in the input file.\"" : "";
            sbBashScript.Replace("__USAGE_INPUT_STATEMENT__", inputOverridesRequired);

            if (parseInputFile.Length > 0)
            {
                parseInputTemplate.Replace("__SCRIPT_NAME__", this.ScriptName);
                parseInputTemplate.Replace("__FILE_TO_SETTINGS__", parseInputFile.ToString());
                sbBashScript.Replace("__PARSE_INPUT_FILE__", parseInputTemplate.ToString());
            }
            else
            {
                sbBashScript.Replace("__PARSE_INPUT_FILE__", "");
            }

            sbBashScript.Replace("__REQUIRED_PARAMETERS__", requiredVariablesTemplate.ToString());
            sbBashScript.Replace("__LOGGING_SUPPORT_", logTemplate.ToString());
            sbBashScript.Replace("__END_LOGGING_SUPPORT__", this.LoggingSupport ? endLogTemplate.ToString() : "");

            if (this.CreateVerifyDelete)
            {
                if (!ScriptData.FunctionExists(this.UserCode, "onVerify") && !ScriptData.FunctionExists(this.UserCode, "onDelete") && !ScriptData.FunctionExists(this.UserCode, "onCreate"))
                {
                    //
                    //  if they don't have the functions, add the template code
                    sbBashScript.Replace("__USER_CODE_1__", verifyCreateDeleteTemplate.ToString());
                }
            }
            //
            // put the user code where it belongs -- it might contain the functions already
            sbBashScript.Replace("__USER_CODE_1__", this.UserCode);

            this.BashScript = sbBashScript.ToString();
            ValidationErrorList.Clear();
            return true;


        }

        /// <summary>
        ///     this generates the JSON that this script needs for an input file
        /// </summary>
        /// <returns></returns>
        public string GetInputJson()
        {

            //       we want something like
            //   "__SCRIPT__NAME__ : {
            //      "longParameter": "Default"
            //  }


            string nl = "\n";


            StringBuilder sb = new StringBuilder($"{Tabs(1)}\"{ScriptName}\": {{{nl}");

            string paramKeyValuePairs = "";
            char[] quotes = { '"' };
            char[] commadNewLine = { ',', '\n', ' ' };
            foreach (ParameterItem param in Parameters)
            {
                string defValue = param.Default;
                defValue = defValue.TrimStart(quotes);
                defValue = defValue.TrimEnd(quotes);
                defValue = defValue.Replace("\\", "\\\\");
                paramKeyValuePairs += $"{Tabs(2)}\"{param.LongParameter}\": \"{defValue}\",{nl}";

            }
            //  delete trailing "," "\n" and spaces
            paramKeyValuePairs = paramKeyValuePairs.TrimEnd(commadNewLine);
            sb.Append(paramKeyValuePairs);

            sb.Append($"{nl}{Tabs(1)}}}");


            return sb.ToString();

        }

        /// <summary>
        ///     generate the JSON needed for VS Code debug config when using the Bash Debug extension
        /// </summary>
        /// <param name="scriptDirectory"></param>
        /// <returns></returns>
        public string VSCodeDebugInfo(string scriptDirectory)
        {
            StringBuilder sb = new StringBuilder();

            try
            {
                string scriptDir = scriptDirectory;
                string scriptName = this.ScriptName;
                char[] slashes = new char[] { '/', '\\' };
                char[] quotes = new char[] { '\"', '\'' };
                scriptDir = scriptDir.TrimEnd(slashes).TrimStart(new char[] { '.', '/' }).TrimEnd(slashes);
                scriptName = scriptName = scriptName.TrimStart(slashes);
                string nl = "\n";
                sb.Append($"{{{nl}");
                sb.Append($"{Tabs(1)}\"type\": \"bashdb\",{nl}");
                sb.Append($"{Tabs(1)}\"request\": \"launch\",{nl}");
                sb.Append($"{Tabs(1)}\"name\": \"Debug {this.ScriptName}\",{nl}");
                sb.Append($"{Tabs(1)}\"cwd\": \"${{workspaceFolder}}\",{nl}");

                sb.Append($"{Tabs(1)}\"program\": \"${{workspaceFolder}}/{scriptDir}/{scriptName}\",{nl}");
                sb.Append($"{Tabs(1)}\"args\": [{nl}");
                foreach (ParameterItem param in Parameters)
                {
                    sb.Append($"{Tabs(2)}\"--{param.LongParameter}\",{nl}{Tabs(2)}\"{param.Default.TrimStart(quotes).TrimEnd(quotes)}\",{nl}");
                }


                sb.Append($"{Tabs(1)}]{nl}");
                sb.Append($"}}");
            }
            catch (Exception e)
            {
                return $"Exception generating config\n\nException Info:\n===============\n{e.Message}";
            }

            return sb.ToString();
        }

      

        /// <summary>
        ///     Given a bash file, create a ScriptData object.  This is the "parse a bash script" function
        /// </summary>
        /// <param name="bash"></param>
        public bool FromBash(string bash)
        {

            try
            {

                
                
                UpdateOnPropertyChanged = false; // this flag stops the NotifyPropertyChanged events from firing
                _doNotGenerateBashScript = true;  // this flag tells everything that we are in the process of parsing
                Reset();
                bash = bash.Replace("\r\n", "\n");
                bash = bash.Replace("\r", "\n");


                //
                //  first look for the bash version
                string versionLine = "# bashWizard version ";
                int index = bash.IndexOf(versionLine);
                double userBashVersion = 0.1;
                string[] lines = null;
                string line = "";
                int count = 0;
                if (index > 0)
                {
                    double.TryParse(bash.Substring(index + versionLine.Length, 5), out userBashVersion);
                }
                else
                {
                    //
                    //  see if it is a BashWizard by looking for the old comments


                    if (GetStringBetween(bash, "# --- END OF BASH WIZARD GENERATED CODE ---", "# --- YOUR SCRIPT ENDS HERE ---", out string code) == false)
                    {
                        ParseErrorList.Add("The Bash Wizard couldn't find the version of this file and it doesn't have the old comment delimiters.  Not a Bash Wizard file.");

                    }
                    else
                    {
                        this.UserCode = code;
                    }

                }

                if (this.UserCode == "") // not an old style script...
                {

                    if (userBashVersion < 0.9)
                    {
                        ParseErrorList.Add($"The Bash Wizard doesn't know how to open version {userBashVersion}.");

                    }

                    if (GetStringBetween(bash, "# --- BEGIN USER CODE ---", "# --- END USER CODE ---", out string userCode) == false)
                    {
                        ParseErrorList.Add("Missing the comments around the user's code.  User Code starts after \"# --- BEGIN USER CODE ---\" and ends before \"# --- END USER CODE ---\" ");


                    }
                    else
                    {
                        this.UserCode = userCode;
                    }
                }

                //
                //  find the usage() function and parse it out - this gives us the 4 properties in the ParameterItem below
                if (GetStringBetween(bash, "usage() {", "exit 1", out string bashFragment) == false)
                {
                    ParseErrorList.Add(bashFragment);

                }
                else
                {
                    bashFragment = bashFragment.Replace("echoWarning", "echo");
                    bashFragment = bashFragment.Replace("\n", "");
                    lines = bashFragment.Split(new string[] { "echo ", "\"" }, StringSplitOptions.RemoveEmptyEntries);
                    line = "";
                    count = 0;
                    foreach (var l in lines)
                    {
                        line = l.Trim();
                        if (line == "")
                        {
                            continue;
                        }
                        count++;
                        if (count == 2)
                        {
                            //
                            //  we write a Warning line, then the description, then instructions
                            //  strip trailing quote and spaces

                            if (!line.StartsWith("Usage: $0")) // to protect from blank Descriptions
                            {
                                this.Description = line.TrimEnd();
                            }
                            continue;
                        }

                        if (line.Substring(0, 1) == "-") // we have a parameter!
                        {
                            string[] paramTokens = line.Split(new string[] { Tabs(1), "|" }, StringSplitOptions.RemoveEmptyEntries);
                            ParameterItem parameterItem = new ParameterItem()
                            {
                                ShortParameter = paramTokens[0].Trim(),
                                LongParameter = paramTokens[1].Trim(),
                                RequiredParameter = (paramTokens[2].Trim() == "Required") ? true : false,
                                Description = paramTokens.Length == 4 ? paramTokens[3].Trim() : "" // strictly speaking, the descirption can be null, and I found that in some scripts
                            };


                            Parameters.Add(parameterItem);
                        }
                    }
                }

                //
                //  parse the echoInput() function to get script name - dont' fail parsing on this one
                bashFragment = "";
                if (GetStringBetween(bash, "echoInput() {", "parseInput()", out bashFragment))
                {
                    lines = bashFragment.Split('\n');
                    foreach (var l in lines)
                    {
                        line = l.Trim();
                        if (line == "")
                        {
                            continue;
                        }
                        //
                        //  the line is in the form of: "echo "<scriptName>:"
                        if (GetStringBetween(line, "echo \"", ":", out string name))
                        {
                            ScriptName = name;
                        }
                        break;
                    }
                }


                //
                //  next parse out the "parseInput" function to get "valueWhenSet" and the "VariableName"

                bashFragment = "";
                if (GetStringBetween(bash, "eval set -- \"$PARSED\"", "--)", out bashFragment) == false)
                {
                    ValidationErrorList.Add(bashFragment);

                }
                else
                {

                    lines = bashFragment.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    for (index = 0; index < lines.Length; index++)
                    {
                        line = lines[index].Trim();
                        if (line == "")
                        {
                            continue;
                        }

                        if (line.Substring(0, 1) == "-") // we have a parameter!
                        {
                            string[] paramTokens = lines[index + 1].Trim().Split(new char[] { '=' });
                            if (paramTokens.Length != 2)
                            {
                                ParseErrorList.Add($"When parsing the parseInput() function to get the variable names, encountered the line {lines[index + 1].Trim()} which doesn't parse.  It should look like varName=$2 or the like.");

                            }
                            string[] nameTokens = line.Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                            if (nameTokens.Length != 2) // the first is the short param, second long param, and third is empty
                            {
                                ParseErrorList.Add($"When parsing the parseInput() function to get the variable names, encountered the line {lines[index].Trim()} which doesn't parse.  It should look like \"-l | --long-name)\" or the like.");
                            }
                            // nameTokens[1] looks like "--long-param)
                            string longParam = nameTokens[1].Substring(3, nameTokens[1].Length - 4);
                            var param = FindParameterByLongName(longParam);
                            if (param == null)
                            {
                                ParseErrorList.Add($"When parsing the parseInput() function to get the variable names, found a long parameter named {longParam} which was not found in the usage() function");
                            }
                            param.VariableName = paramTokens[0].Trim();
                            param.ValueIfSet = paramTokens[1].Trim();
                            if (lines[index + 2].Trim() == "shift 1")
                            {
                                param.RequiresInputString = false;
                            }
                            else if (lines[index + 2].Trim() == "shift 2")
                            {
                                param.RequiresInputString = true;
                            }
                            else
                            {
                                ParseErrorList.Add($"When parsing the parseInput() function to see if {param.VariableName} requires input, found this line: {lines[index + 1]} which does not parse.  it should either be \"shift 1\" or \"shift 2\"");
                            }

                            index += 2;
                        }
                    }
                }
                // the last bit of info to suss out is the default value -- find these with a comment
                if (GetStringBetween(bash, "# input variables", "parseInput", out bashFragment) == false)
                {
                    ParseErrorList.Add(bashFragment);
                }
                else
                {
                    // throw away the "declare "
                    bashFragment = bashFragment.Replace("declare ", "");
                    lines = bashFragment.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var l in lines)
                    {
                        line = l.Trim();
                        if (line == "")
                        {
                            continue;
                        }
                        if (line.StartsWith("#"))
                        {
                            continue;
                        }

                        string[] varTokens = line.Split(new char[] { '=' }, StringSplitOptions.RemoveEmptyEntries);
                        if (varTokens.Length == 0 || varTokens.Length > 2)
                        {
                            ParseErrorList.Add($"When parsing the variable declarations between the \"# input variables\" comment and the \"parseInput\" calls, the line {line} was encountered that didn't parse.  it should be in the form of varName=Default");

                        }
                        var varName = varTokens[0].Trim();
                        var param = FindParameterByVarName(varName);
                        if (param == null)
                        {
                            ParseErrorList.Add($"When parsing the variable declarations between the \"# input variables\" comment and the \"parseInput\" calls, found a variable named {varName} which was not found in the usage() function");

                        }
                        else
                        {
                            param.Default = varTokens.Length == 2 ? varTokens[1].Trim() : "";  // in bash "varName=" is a valid declaration
                        }

                    }
                }


                return ParseErrorList.Count == 0;
            }
            finally
            {
                //
                //  need to update everything that might have been changed by the parse
                UpdateOnPropertyChanged = true; // force events to fire
                NotifyPropertyChanged("Description");
                NotifyPropertyChanged("ScriptName");


                //  "BashScript" also updates the ToggleButtons
                _doNotGenerateBashScript = false; // setting this hear makes it so we don't generate the script when we change the Description and the Name
                NotifyPropertyChanged("BashScript");
              



            }



        }
    }
}