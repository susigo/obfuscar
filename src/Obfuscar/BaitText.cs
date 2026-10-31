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

namespace Obfuscar
{
    /// <summary>
    /// Bait and safeguard word lists. The sensitive strings never exist in
    /// this repository as readable text: BaitText.Data.cs (generated offline
    /// by build/Encode-BaitText.ps1) carries only a shuffled character table
    /// and per-word index sequences. The words are reassembled here, once,
    /// when the obfuscator first touches the lists.
    /// </summary>
    internal static partial class BaitText
    {
        // Assigned in the static ctor: field initializer order across partial
        // declarations is unspecified, so the packed arrays may still be null
        // while initializers in this file run.
        internal static readonly string[] ArchitecturePrefixes;
        internal static readonly string[] ArchitectureSuffixes;
        internal static readonly string[] BaitTypeNames;
        internal static readonly string[] MethodVerbs;
        internal static readonly string[] MethodNouns;
        internal static readonly string[] BaitMethodNames;
        internal static readonly string[] FieldNames;
        internal static readonly string[] SubNamespaces;
        internal static readonly string[] StringPool;
        internal static readonly string[] CovertStringPool;
        internal static readonly string[] ShortDecoys;
        internal static readonly string[] GateFieldNames;
        internal static readonly string[] SensitivePool;

        static BaitText()
        {
            ArchitecturePrefixes = UnpackAll(ArchitecturePrefixesData);
            ArchitectureSuffixes = UnpackAll(ArchitectureSuffixesData);
            BaitTypeNames = UnpackAll(BaitTypeNamesData);
            MethodVerbs = UnpackAll(MethodVerbsData);
            MethodNouns = UnpackAll(MethodNounsData);
            BaitMethodNames = UnpackAll(BaitMethodNamesData);
            FieldNames = UnpackAll(FieldNamesData);
            SubNamespaces = UnpackAll(SubNamespacesData);
            StringPool = UnpackAll(StringPoolData);
            CovertStringPool = UnpackAll(CovertStringPoolData);
            ShortDecoys = UnpackAll(ShortDecoysData);
            GateFieldNames = UnpackAll(GateFieldNamesData);
            SensitivePool = UnpackAll(SensitivePoolData);
        }

        private static string[] UnpackAll(string[] packed)
        {
            var words = new string[packed.Length];
            for (int i = 0; i < packed.Length; i++)
                words[i] = Unpack(packed[i]);
            return words;
        }

        private static string Unpack(string packed)
        {
            var bytes = Convert.FromBase64String(packed);
            var chars = new char[bytes.Length >> 1];
            for (int i = 0; i < chars.Length; i++)
                chars[i] = Pool[bytes[i << 1] | (bytes[(i << 1) + 1] << 8)];
            return new string(chars);
        }
    }
}
