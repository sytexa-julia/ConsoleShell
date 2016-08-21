#region Copyright
//  SYTEXA LLC ("SYTEXA") CONFIDENTIAL
//  Unpublished Copyright (c) 2015-2016 SYTEXA, All Rights Reserved.
// 
//  NOTICE:  All information contained herein is, and remains the property of SYTEXA. The intellectual and technical concepts contained
//  herein are proprietary to SYTEXA and may be covered by U.S. and Foreign Patents, patents in process, and are protected by trade secret or copyright law.
//  Dissemination of this information or reproduction of this material is strictly forbidden unless prior written permission is obtained
//  from SYTEXA.  Access to the source code contained herein is hereby forbidden to anyone except current SYTEXA employees, managers or contractors who have executed 
//  Confidentiality and Non-disclosure agreements explicitly covering such access.
// 
//  The copyright notice above does not evidence any actual or intended publication or disclosure  of  this source code, which includes  
//  information that is confidential and/or proprietary, and is a trade secret, of  SYTEXA.   ANY REPRODUCTION, MODIFICATION, DISTRIBUTION, PUBLIC  PERFORMANCE, 
//  OR PUBLIC DISPLAY OF OR THROUGH USE  OF THIS  SOURCE CODE  WITHOUT  THE EXPRESS WRITTEN CONSENT OF SYTEXA IS STRICTLY PROHIBITED, AND IN VIOLATION OF APPLICABLE 
//  LAWS AND INTERNATIONAL TREATIES.  THE RECEIPT OR POSSESSION OF  THIS SOURCE CODE AND/OR RELATED INFORMATION DOES NOT CONVEY OR IMPLY ANY RIGHTS  
//  TO REPRODUCE, DISCLOSE OR DISTRIBUTE ITS CONTENTS, OR TO MANUFACTURE, USE, OR SELL ANYTHING THAT IT  MAY DESCRIBE, IN WHOLE OR IN PART.                
// 
//  This file is part of ConsoleShell.
#endregion
namespace ConsoleShell
{
    /// <summary>
    /// Defines an interface for a preprocessor stage for command input parsing.
    /// </summary>
    public interface ICommandPreprocessorStage
    {
        /// <summary>
        /// Gets the priority of the stage. Lower priority stages are executed first.
        /// Stages with the same priority are executed in the order registered to the Shell.
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Preprocesses a command and returns the (optionally) transformed command tokens.
        /// </summary>
        /// <param name="shell">The <c>Shell</c> instance</param>
        /// <param name="tokens">The tokens that make up the command.</param>
        /// <returns>The command tokens, optionally transformed during preprocessing</returns>
        string[] PreprocessCommand(Shell shell, string[] tokens);

        /// <summary>
        /// Removes preprocessor syntax elements from the provided command text and returns
        /// the modified command text.
        /// </summary>
        /// <param name="input">The command input (so far), potentially containing preprocessor syntax elements</param>
        /// <returns>The input command text, with any syntax specified to the implemented 
        /// preprocessor stage removed</returns>
        string RemovePreprocessorSyntax(string input);
    }
}