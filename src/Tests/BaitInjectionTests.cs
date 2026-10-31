#region Copyright (c) 2007 Ryan Williams <drcforbin@gmail.com>

/// <copyright>
/// Copyright (c) 2007 Ryan Williams <drcforbin@gmail.com>
/// 
/// Permission is hereby granted, free of charge, to any person obtaining a copy
/// of this software and associated documentation files (the "Software"), to deal
/// in the Software without restriction, including without limitation the rights
/// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
/// copies of the Software, and to permit persons to whom the Software is
/// furnished to do so, subject to the following conditions:
/// 
/// The above copyright notice and this permission notice shall be included in
/// all copies or substantial portions of the Software.
/// 
/// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
/// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
/// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
/// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
/// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
/// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
/// THE SOFTWARE.
/// </copyright>

#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LeXtudio.Metadata.Mutable;
using Xunit;

namespace ObfuscarTests
{
    public class BaitInjectionTests
    {
        private static string BuildXml(string outputPath, bool injectBait, string encoding = "plain")
        {
            return string.Format(
                @"<?xml version='1.0'?>" +
                @"<Obfuscator>" +
                @"<Var name='InPath' value='{0}' />" +
                @"<Var name='OutPath' value='{1}' />" +
                @"<Var name='HideStrings' value='false' />" +
                @"<Var name='InjectBait' value='{2}' />" +
                @"<Var name='BaitTypeCount' value='40' />" +
                @"<Var name='BaitMethodsPerType' value='6' />" +
                @"<Var name='BaitSeed' value='1337' />" +
                @"<Var name='BaitStringEncoding' value='{4}' />" +
                @"<Module file='{0}{3}AssemblyWithStrings.dll' />" +
                @"</Obfuscator>",
                TestHelper.InputPath, outputPath,
                injectBait ? "true" : "false", Path.DirectorySeparatorChar,
                encoding);
        }

        private static List<TypeDefinition> GetBaitTypes(AssemblyDefinition assembly)
        {
            return assembly.MainModule.Types
                .Where(t => t.FullName.StartsWith("AssemblyWithStrings."))
                .ToList();
        }

        [Fact]
        public void CheckNoBaitWhenDisabled()
        {
            string outputPath = TestHelper.OutputPath;
            TestHelper.BuildAndObfuscate("AssemblyWithStrings", string.Empty,
                BuildXml(outputPath, false), false,
                Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp7);

            var assmDef = AssemblyDefinition.ReadAssembly(
                Path.Combine(outputPath, "AssemblyWithStrings.dll"));

            Assert.Equal(5, assmDef.MainModule.Types.Count);
            Assert.Empty(GetBaitTypes(assmDef));
        }

        [Fact]
        public void CheckBaitTypesMethodsAndStringsInjected()
        {
            string outputPath = TestHelper.OutputPath;
            TestHelper.BuildAndObfuscate("AssemblyWithStrings", string.Empty,
                BuildXml(outputPath, true), false,
                Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp7);

            var assmDef = AssemblyDefinition.ReadAssembly(
                Path.Combine(outputPath, "AssemblyWithStrings.dll"));

            var baitTypes = GetBaitTypes(assmDef);
            Assert.True(baitTypes.Count >= 40,
                $"Expected at least 40 bait types, found {baitTypes.Count}");

            var baitMethodSet = new HashSet<MethodDefinition>(
                baitTypes.SelectMany(t => t.Methods));

            int baitMethodCount = 0;
            int stringPayloads = 0;
            int callGraphEdges = 0;
            int switchCount = 0;

            foreach (var method in baitMethodSet)
            {
                if (method.Body == null)
                    continue;

                baitMethodCount++;
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode == OpCodes.Ldstr)
                        stringPayloads++;
                    else if (instruction.OpCode == OpCodes.Switch)
                        switchCount++;
                    else if (instruction.OpCode == OpCodes.Call &&
                             instruction.Operand is MethodDefinition callee &&
                             baitMethodSet.Contains(callee))
                        callGraphEdges++;
                }
            }

            // 40 types * 6 methods = 240 bodies, plus one .cctor per type
            Assert.True(baitMethodCount >= 240,
                $"Expected at least 240 bait methods, found {baitMethodCount}");

            // every method embeds >= 2 long strings and each type's .cctor adds more
            Assert.True(stringPayloads >= 400,
                $"Expected at least 400 injected string payloads, found {stringPayloads}");

            // bait->bait calls form the fake call graph
            Assert.True(callGraphEdges > 0,
                "Expected bait methods to call each other");

            // some methods should carry the mini state-machine
            Assert.True(switchCount > 0, "Expected switch-based noise methods");

            // bait types must keep their juicy names - renaming must not touch them
            var juicyNames = new HashSet<string>(global::Obfuscar.BaitText.BaitTypeNames);
            Assert.Contains(baitTypes,
                t => juicyNames.Contains(t.Name) ||
                     t.Name.StartsWith("License") ||
                     t.Name.EndsWith("Manager") ||
                     t.Name.EndsWith("Service"));
        }

        [Fact]
        public void CheckCovertBase64Payloads()
        {
            string outputPath = TestHelper.OutputPath;
            TestHelper.BuildAndObfuscate("AssemblyWithStrings", string.Empty,
                BuildXml(outputPath, true, "base64"), false,
                Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp7);

            var assmDef = AssemblyDefinition.ReadAssembly(
                Path.Combine(outputPath, "AssemblyWithStrings.dll"));

            var baitTypes = GetBaitTypes(assmDef);
            Assert.NotEmpty(baitTypes);

            int fromBase64Calls = 0;
            var decoded = new List<string>();

            foreach (var method in baitTypes.SelectMany(t => t.Methods))
            {
                if (method.Body == null)
                    continue;

                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode == OpCodes.Call &&
                        instruction.Operand is MethodReference callee &&
                        callee.Name == "FromBase64String")
                        fromBase64Calls++;

                    if (instruction.OpCode == OpCodes.Ldstr &&
                        instruction.Operand is string literal)
                    {
                        try
                        {
                            var text = System.Text.Encoding.UTF8.GetString(
                                Convert.FromBase64String(literal));
                            if (text.Contains("AI") || text.Contains("LANGUAGE MODEL") ||
                                text.Contains("ref 0x"))
                                decoded.Add(text);
                        }
                        catch (FormatException)
                        {
                        }
                    }
                }
            }

            Assert.True(fromBase64Calls > 0, "Expected Convert.FromBase64String call sites");
            Assert.True(decoded.Count >= 40,
                $"Expected many decodable covert payloads, found {decoded.Count}");
        }

        [Fact]
        public void CheckBaitWovenIntoRealMethods()
        {
            string outputPath = TestHelper.OutputPath;
            TestHelper.BuildAndObfuscate("AssemblyWithStrings", string.Empty,
                BuildXml(outputPath, true), false,
                Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp7);

            var assmDef = AssemblyDefinition.ReadAssembly(
                Path.Combine(outputPath, "AssemblyWithStrings.dll"));

            var baitFullNames = new HashSet<string>(
                GetBaitTypes(assmDef).Select(t => t.FullName));

            var realTypes = assmDef.MainModule.Types
                .Where(t => !t.FullName.StartsWith("AssemblyWithStrings.") &&
                            t.FullName != "<Module>")
                .ToList();

            int realToBaitCalls = 0;
            int gateReads = 0;

            foreach (var method in realTypes.SelectMany(t => t.Methods))
            {
                if (method.Body == null)
                    continue;

                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode == OpCodes.Ldsfld &&
                        instruction.Operand is FieldReference field &&
                        field.DeclaringType != null &&
                        baitFullNames.Contains(field.DeclaringType.FullName))
                        gateReads++;

                    if (instruction.OpCode == OpCodes.Call &&
                        instruction.Operand is MethodReference callee &&
                        callee.DeclaringType != null &&
                        baitFullNames.Contains(callee.DeclaringType.FullName))
                        realToBaitCalls++;
                }
            }

            Assert.True(gateReads > 0, "Expected real methods to read bait gate fields");
            Assert.True(realToBaitCalls > 0,
                "Expected real methods to call bait methods");
            // each injected block = exactly 1 gate read + 1-3 calls
            Assert.True(realToBaitCalls >= gateReads,
                "Expected at least one bait call per guard predicate");
        }

        [Fact]
        public void CheckEnhancedBaitPoolsWithSensitive()
        {
            string outputPath = TestHelper.OutputPath;
            var xml = string.Format(
                @"<?xml version='1.0'?>" +
                @"<Obfuscator>" +
                @"<Var name='InPath' value='{0}' />" +
                @"<Var name='OutPath' value='{1}' />" +
                @"<Var name='HideStrings' value='false' />" +
                @"<Var name='InjectBait' value='true' />" +
                @"<Var name='BaitTypeCount' value='60' />" +
                @"<Var name='BaitMethodsPerType' value='6' />" +
                @"<Var name='BaitSeed' value='2024' />" +
                @"<Var name='BaitSensitive' value='true' />" +
                @"<Module file='{0}{2}AssemblyWithStrings.dll' />" +
                @"</Obfuscator>",
                TestHelper.InputPath, outputPath, Path.DirectorySeparatorChar);

            TestHelper.BuildAndObfuscate("AssemblyWithStrings", string.Empty,
                xml, false, Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp7);

            var assmDef = AssemblyDefinition.ReadAssembly(
                Path.Combine(outputPath, "AssemblyWithStrings.dll"));

            var baitTypes = GetBaitTypes(assmDef);
            var allStrings = new List<string>();
            var allMethodNames = new List<string>();

            foreach (var method in baitTypes.SelectMany(t => t.Methods))
            {
                allMethodNames.Add(method.Name);
                if (method.Body == null) continue;
                foreach (var instr in method.Body.Instructions)
                {
                    if (instr.OpCode == OpCodes.Ldstr && instr.Operand is string s)
                        allStrings.Add(s);
                }
            }

            bool hasCredential = allStrings.Any(s =>
                s.Contains("AWS_") || s.Contains("GITHUB_TOKEN") ||
                s.Contains("API_KEY") || s.Contains("AKIA") ||
                s.Contains("ghp_") || s.Contains("sk-") ||
                s.Contains("STRIPE_") || s.Contains("DATABASE_URL"));
            Assert.True(hasCredential,
                "Expected fake credential strings when BaitSensitive=true");

            bool hasLegalThreat = allStrings.Any(s =>
                s.Contains("DMCA") || s.Contains("CFAA") ||
                s.Contains("honeypot") || s.Contains("HONEYPOT") ||
                s.Contains("reverse engineer"));
            Assert.True(hasLegalThreat,
                "Expected legal threat strings when BaitSensitive=true");

            bool hasMalwareName = allMethodNames.Any(n =>
                n.Contains("Capture") || n.Contains("Exfiltrate") ||
                n.Contains("Beacon") || n.Contains("Keystrokes") ||
                n.Contains("Credentials") || n.Contains("Shell"));
            Assert.True(hasMalwareName,
                "Expected malware-signature method names when BaitSensitive=true");
        }

        [Fact]
        public void CheckBaitNamesSurviveRenaming()
        {
            string outputPath = TestHelper.OutputPath;
            TestHelper.BuildAndObfuscate("AssemblyWithStrings", string.Empty,
                BuildXml(outputPath, true), false,
                Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp7);

            var assmDef = AssemblyDefinition.ReadAssembly(
                Path.Combine(outputPath, "AssemblyWithStrings.dll"));

            var names = GetBaitTypes(assmDef).Select(t => t.Name).ToList();

            // single-letter or punctuation-mangled names would mean the renamer
            // ate the bait - every injected name must stay readable (and juicy)
            Assert.All(names, n => Assert.True(n.Length > 8,
                $"Bait type name '{n}' looks renamed"));
        }
    }
}
