using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using ConsoleShell.Internal;
using ConsoleShell.Readline;

namespace ConsoleShell
{
    public class Shell
    {
        #region Fields

        private ShellCommandsContainer _container = new ShellCommandsContainer();
        private readonly object _lock = new object();        

        #endregion

        private ICollection<ICommandPreprocessorStage> PreprocessorStages = new List<ICommandPreprocessorStage>();

        /// <summary>
        /// Stores the result of an executed command. 
        /// Should be set in a <c>IShellCommand</c> <c>Invoke</c> method.
        /// The value is set to <c>null</c> at the start of the next <c>ExecuteCommand</c> call.
        /// </summary>
        public object CommandResult { get; set; }

        public event EventHandler<CommandNotFoundEventArgs> ShellCommandNotFound;
        public event EventHandler<PrintAlternativesEventArgs> PrintAlternatives;
        public event EventHandler ShellInterrupt;
        public event EventHandler WritePrompt;
        public event EventHandler<CommandExecuteEventArgs> AfterCommandExecute;
        public event EventHandler<CommandExecuteEventArgs> BeforeCommandExecute;
        
        public bool CtrlCInterrupts { get; set; } = Path.DirectorySeparatorChar == '/';
        public bool CtrlDIsEOF { get; set; } = true;
        public bool CtrlZIsEOF { get; set; } = Path.DirectorySeparatorChar == '\\';

        public ShellHistory History { get; private set; }

        /// <summary>
        /// The default completion formatter. 
        /// </summary>
        public static readonly Func<string[], string[]> DefaultCompletionFormatter = (string[] strings) =>
        {
            if (strings.Count() == 1)
                return new string[] {$"{strings[0]} "};

            return strings;
        };

        /// <summary>
        /// The default completions printer action.
        /// </summary>
        public static readonly Action<string[]> DefaultCompletionsPrinter = (string[] strings) =>
        {
            Console.WriteLine("Possible completions:");
            foreach (var item in strings)
                Console.WriteLine($"- {item}");
        };

        /// <summary>
        /// A completions printer action that prints out the input with no formatting other than
        /// a leading and a tailing blank line.
        /// </summary>
        public static readonly Action<string[]> SimpleCompletionsPrinter = (string[] strings) =>
        {
            Console.WriteLine();
            foreach (var item in strings)
                Console.WriteLine(item);
            Console.WriteLine();
        };

        /// <summary>
        /// Gets or sets a formatter action for Alternatives/tab completion options.
        /// If set, it will be used once, the next time alternatives/tab completion is completed.
        /// Then the DefaultCompletionFormatter will be restored.
        /// </summary>
        public Func<string[], string[]> CompletionFormatter { get; set; }

        /// <summary>
        /// Gets or sets an action for displaying the completions / alternative options.
        /// The <c>PrintAlternatives</c> event takes precedence over this.
        /// If set, it will be used once, the next time alternatives/tab completion options need
        /// to be displayed. Then the <c>DefaultCompletionsPrinter</c> will be restored.
        /// </summary>
        public Action<string[]> CompletionPrinter { get; set; }

        public Shell()
        {
            History = new ShellHistory();
            CompletionFormatter = DefaultCompletionFormatter;
            CompletionPrinter = DefaultCompletionsPrinter;
        }

        public Shell(ShellHistory historyManager)
        {
            History = historyManager;
            CompletionFormatter = DefaultCompletionFormatter;
            CompletionPrinter = DefaultCompletionsPrinter;
        }

        /// <summary>
        /// Checks if the specified input tokens resolve to a registered shell command.
        /// </summary>
        /// <param name="tokens">Input tokens</param>
        /// <returns><c>true</c> if there is a registered command that can handle the given input, <c>false</c> otherwise</returns>
        public bool IsValidCommand(string[] tokens)
        {
            lock (_lock)
            {
                return null != _container.FindCommandAction(this, tokens);
            }
        }

        public void RunShell()
        {
            var readline = new Readline.Readline(History)
            {
                CtrlCInterrupts = CtrlCInterrupts,
                CtrlDIsEOF = CtrlDIsEOF,
                CtrlZIsEOF = CtrlZIsEOF
            };

            readline.WritePrompt += ReadlineOnWritePrompt;
            readline.Interrupt += (sender, args) => ShellInterrupt?.Invoke(this, EventArgs.Empty);
            readline.TabComplete += ReadlineOnTabComplete;
            readline.PrintAlternatives += (sender, args) => OnPrintAlternatives(args);

            while (true)
            {
                var input = readline.ReadLine();                

                if (string.IsNullOrWhiteSpace(input))
                {
                    continue;
                }

                input = input.Trim();

                try
                {
                    var tokens = ShellCommandTokenizer.Tokenize(input).ToArray();

                    IShellCommand command = null;
                    lock (_lock)
                    {
                        command = _container.FindCommand(this, tokens);
                    }

                    BeforeCommandExecute?.Invoke(this, new CommandExecuteEventArgs(input, null, command));
                    ExecuteCommand(tokens);
                    AfterCommandExecute?.Invoke(this, new CommandExecuteEventArgs(input, CommandResult, command));
                }
                catch (ShellCommandNotFoundException)
                {
                    OnShellCommandNotFound(input);
                }

                History.AddUnique(input);
            }
        }

        private void ReadlineOnWritePrompt(object sender, EventArgs eventArgs)
        {
            var handler = WritePrompt;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
            else
            {
                Console.Write(">");
            }
        }

        public virtual string ReadPassword(Action writePromtAction)
        {
            var readline = new Readline.Readline(History)
            {
                CtrlCInterrupts = CtrlCInterrupts,
                CtrlDIsEOF = CtrlDIsEOF,
                CtrlZIsEOF = CtrlZIsEOF
            };

            readline.WritePrompt += (sender, args) => WritePrompt?.Invoke(this, EventArgs.Empty);
            readline.Interrupt += (sender, args) => ShellInterrupt?.Invoke(this, EventArgs.Empty);

            return readline.ReadPassword();
        }

        private void ReadlineOnTabComplete(object sender, TabCompleteEventArgs e)
        {
            var buff = ((Readline.Readline)sender).LineBuffer;

            // Remove preprocessor syntax that the command processor doesn't know about
            foreach (var pp in PreprocessorStages.OrderBy(s => s.Priority))
                buff = pp.RemovePreprocessorSyntax(buff);

            lock (_lock)
            {
                var complete = _container.CompleteInput(this, buff).ToArray();
                string[] formattedComplete = null;

                if (CompletionFormatter != null)
                {
                    formattedComplete = CompletionFormatter(complete) ?? complete;
                    CompletionFormatter = DefaultCompletionFormatter;
                }

                if (complete.Length == 1)
                {                    
                    e.Output = formattedComplete.First();
                }
                else if (complete.Length > 1)
                {
                    var commonPrefix = FindCommonPrefix(complete);
                    if (!string.IsNullOrEmpty(commonPrefix) 
                        && !buff.EndsWith(commonPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        e.Output = commonPrefix; // Auto-fill as much as possible
                    }
                    else
                    {
                        e.Alternatives = formattedComplete;
                    }
                }
            }
        }

        /// <summary>
        /// Finds a prefix string common to all values in <c>options</c>.
        /// </summary>
        /// <param name="options">Array of strings</param>
        /// <returns>The prefix common to all values in <c>options</c>, or null if there is none.</returns>
        private string FindCommonPrefix(string[] options)
        {
            string commonPrefix = null;
            string testPrefix = "";
            foreach (string opt in options)
            {
                foreach (var ch in opt)
                {
                    testPrefix += ch;
                    if (!options.All(c => c.StartsWith(testPrefix, StringComparison.OrdinalIgnoreCase)))
                    {
                        break;
                    }

                    commonPrefix = testPrefix;
                }
            }

            return commonPrefix;
        }

        #region Execute

        private string CollectionToString<T>(IEnumerable<T> collection, string separater = " ")
        {
            var builder = new StringBuilder();
            foreach (var i in collection)
            {
                builder.Append(i);
                if (!i.Equals(collection.LastOrDefault()))
                    builder.Append(separater);
            }

            return builder.ToString();
        }

        public virtual void ExecuteCommand(string[] tokens)
        {
            // Clear stored command result, if any.
            CommandResult = null;

            var modTokens = (string[])tokens.Clone();

            // Execute any preprocessor stages
            if (PreprocessorStages?.Any() ?? false)
            {
                Debug.WriteLine($"Begin preprocessing of command with tokens: {CollectionToString(modTokens)}");

                foreach (var ps in PreprocessorStages.OrderBy(s => s.Priority))
                {
                    Debug.WriteLine($"Begin preprocessing stage {ps.GetType().Name} with Priority {ps.Priority}.");
                    Debug.WriteLine($"Tokens before stage: {CollectionToString(modTokens)}");

                    modTokens = ps.PreprocessCommand(this, modTokens);

                    Debug.WriteLine($"Tokens after stage: {CollectionToString(modTokens)}");
                }
                
                Debug.WriteLine($"Final tokens after all preprocessing stages: {CollectionToString(modTokens)}");
            }
            else
            {
                Debug.WriteLine($"No preprocessor stages registered. Executing the command with the provided tokens: {CollectionToString(modTokens)}.");
            }

            // Find and execute the command
            Action command;
            lock (_lock)
            {
                command = _container.FindCommandAction(this, modTokens);
            }

            if (command == null)
            {
                throw new ShellCommandNotFoundException();
            }

            command();
        }

        public virtual void ExecuteCommand(string input)
        {
            ExecuteCommand(ShellCommandTokenizer.Tokenize(input).ToArray());
        }

        #endregion

        #region Commands manipulation

        public virtual SortedList<string, string> GetCommandsDescriptions(string prefix = null)
        {
            lock (_lock)
            {
                return _container.GetDescriptions(prefix);
            }
        }

        public virtual Shell AddCommand(IShellCommand command)
        {
            lock (_lock)
            {
                _container.AddCommand(command);
            }
            return this;
        }

        public virtual Shell ClearCommands()
        {
            lock (_lock)
            {
                _container = new ShellCommandsContainer();
            }
            return this;
        }

        #endregion

        protected virtual void OnShellCommandNotFound(string input)
        {

            var handler = ShellCommandNotFound;

            if (handler != null)
            {
                handler.Invoke(this, new CommandNotFoundEventArgs(input));
            }
            else
            {
                Console.WriteLine("UserInput not found: {0}", input);
                AfterCommandExecute?.Invoke(this, new CommandExecuteEventArgs(input, null));
            }
        }

        protected virtual void OnPrintAlternatives(PrintAlternativesEventArgs e)
        {
            var handler = PrintAlternatives;
            if (handler != null)
            {
                handler(this, e);
            }
            else
            {
                if (CompletionPrinter != null)
                {
                    CompletionPrinter(e.Alternatives);
                    CompletionPrinter = DefaultCompletionsPrinter;
                }
                else
                {
                    DefaultCompletionsPrinter(e.Alternatives);
                }
            }
        }

        /// <summary>
        /// Adds a preprocessor stage to the command interpreter.
        /// </summary>
        /// <param name="preprocessorStage"></param>
        public void RegisterPreprocessorStage(ICommandPreprocessorStage preprocessorStage)
        {
            PreprocessorStages.Add(preprocessorStage);
        }
    }
}
