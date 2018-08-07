﻿using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using TechTalk.SpecFlow.Generator;
using TechTalk.SpecFlow.Generator.UnitTestProvider;
using TechTalk.SpecFlow.Utils;

namespace Unickq.SpecFlow.Selenium.Helpers
{
    public class UnickqSpecFlowSeleniumGeneratorProvider : IUnitTestGeneratorProvider
    {
        private const string TestFixtureAttr = "NUnit.Framework.TestFixtureAttribute";
        private const string TestFixtureSetupAttr = "NUnit.Framework.OneTimeSetUp";
        private const string TestFixtureTearDownAttr = "NUnit.Framework.OneTimeTearDown";

        private const string TestSetupAttr = "NUnit.Framework.SetUpAttribute";
        private const string TestTearDownAttr = "NUnit.Framework.TearDownAttribute";

        private const string TestAttr = "NUnit.Framework.TestAttribute";
        private const string RowAttr = "NUnit.Framework.TestCaseAttribute";
        private const string CategoryAttr = "NUnit.Framework.CategoryAttribute";
        private const string IgnoreAttr = "NUnit.Framework.IgnoreAttribute";
        private const string ParallelizableAttr = "NUnit.Framework.ParallelizableAttribute";
        private const string DescriptionAttr = "NUnit.Framework.DescriptionAttribute";

        private readonly CodeDomHelper _codeDomHelper;

        /// <summary>
        ///     List of unique field Names to Generate
        /// </summary>
        private readonly HashSet<string> _fieldsToGenerate = new HashSet<string>();

        /// <summary>
        ///     Initialization Methods to Generate. MethodName => List of Argument Names
        /// </summary>
        private readonly Dictionary<string, List<string>> _initializeMethodsToGenerate =
            new Dictionary<string, List<string>>();

        private bool _hasBrowser;
        private bool _scenarioSetupMethodsAdded;

        public UnickqSpecFlowSeleniumGeneratorProvider(CodeDomHelper codeDomHelper)
        {
            _codeDomHelper = codeDomHelper;
        }

        public void SetTestMethodCategories(TestClassGenerationContext generationContext,
            CodeMemberMethod testMethod, IEnumerable<string> scenarioCategories)
        {
            var categories = scenarioCategories as IList<string> ?? scenarioCategories.ToList();
            _codeDomHelper.AddAttributeForEachValue(testMethod, CategoryAttr,
                categories.Where(cat => !cat.StartsWith("Browser:") && !cat.Contains(":")));

            var categoryTags = new Dictionary<string, List<string>>();

            var hasTags = false;


            foreach (var tag in categories.Where(cat => cat.Contains(":")).Select(cat => cat.Split(':')))
            {
                if (tag.Length != 2)
                    continue;
                hasTags = true;
                if (tag[0].Equals("Browser", StringComparison.OrdinalIgnoreCase))
                    _hasBrowser = true;
                testMethod.UserData.Add(tag[0] + ":" + tag[1], tag[1]);
                List<string> tagValues;
                if (!categoryTags.TryGetValue(tag[0], out tagValues))
                {
                    tagValues = new List<string>();
                    categoryTags[tag[0]] = tagValues;
                }

                tagValues.Add(tag[1]);
            }

            if (hasTags)
            {
                //TestName and TestCategory Building
                //List of list of tags different values
                var values = new List<List<string>>();
                foreach (var kvp in categoryTags)
                    values.Add(kvp.Value);
                var combinations = new List<List<string>>();
                //Generate an exhaustive list of values combinations
                GeneratePermutations(values, combinations, 0, new List<string>());

                foreach (var combination in combinations)
                {
                    //Each combination is a different TestCase
                    var withTagArgs =
                        combination.Select(s => new CodeAttributeArgument(new CodePrimitiveExpression(s))).ToList()
                            .Concat(new[]
                            {
                                new CodeAttributeArgument("Category",
                                    new CodePrimitiveExpression(string.Join(",", combination))),
                                new CodeAttributeArgument("TestName",
                                    new CodePrimitiveExpression(
                                        $"{testMethod.Name} with {string.Join(",", combination)}"))
                            })
                            .ToArray();

                    _codeDomHelper.AddAttribute(testMethod, RowAttr, withTagArgs);
                }

                var i = 0;

                var orderedTags = new List<string>();
                foreach (var kvp in categoryTags)
                {
                    //Add the category name to category list
                    orderedTags.Add(kvp.Key);
                    //Mark the field to be generated
                    _fieldsToGenerate.Add(kvp.Key);
                    //Add a parameter to the testMethod
                    testMethod.Parameters.Insert(i,
                        new CodeParameterDeclarationExpression("System.String",
                            kvp.Key.ToLowerInvariant()));
                    i = i + 1;
                }

                foreach (var field in _fieldsToGenerate)
                    if (!field.Equals("Browser", StringComparison.OrdinalIgnoreCase))
                    {
                        testMethod.Statements.Insert(4,
                            GenerateCodeSnippetStatement(
                                $"testRunner.ScenarioContext.Add(\"{field}\", {field.ToLower()});"));
                    }
            }
        }

        public void SetRow(TestClassGenerationContext generationContext,
            CodeMemberMethod testMethod, IEnumerable<string> arguments, IEnumerable<string> tags, bool isIgnored)
        {
            var args = arguments.Select(arg => new CodeAttributeArgument(new CodePrimitiveExpression(arg))).ToList();

            var exampleTagExpressionList = tags.Select(t => new CodePrimitiveExpression(t)).ToArray();
            var exampleTagsExpression = exampleTagExpressionList.Length == 0
                ? (CodeExpression) new CodePrimitiveExpression(null)
                : new CodeArrayCreateExpression(typeof(string[]), exampleTagExpressionList);
            args.Add(new CodeAttributeArgument(exampleTagsExpression));

            if (isIgnored) args.Add(new CodeAttributeArgument("Ignored", new CodePrimitiveExpression(true)));

            var categories = testMethod.UserData.Keys.OfType<string>()
                .Where(key => key.Contains(":"));

            var userDataKeys = categories as IList<string> ?? categories.ToList();
            if (userDataKeys.Any())
            {
                //List of list of tags different values
                var values = new Dictionary<string, List<string>>();
                foreach (var userDataKey in userDataKeys)
                {
                    var catName = userDataKey.Substring(0, userDataKey.IndexOf(':'));
                    List<string> val;
                    if (!values.TryGetValue(catName, out val))
                    {
                        val = new List<string>();
                        values[catName] = val;
                    }

                    val.Add((string) testMethod.UserData[userDataKey]);
                }

                var combinations = new List<List<string>>();
                //Generate an exhaustive list of values combinations
                GeneratePermutations(values.Values.ToList(), combinations, 0, new List<string>());

                //Remove TestCase attributes
                foreach (var codeAttributeDeclaration in testMethod.CustomAttributes.Cast<CodeAttributeDeclaration>()
                    .Where(attr => attr.Name == RowAttr && attr.Arguments.Count == 2 + values.Keys.Count).ToList())
                    testMethod.CustomAttributes.Remove(codeAttributeDeclaration);

                foreach (var combination in combinations)
                {
                    var argsString = string.Concat(args.Take(args.Count - 1).Select(arg =>
                        $"\"{((CodePrimitiveExpression) arg.Value).Value}\" ,"));
                    argsString = argsString.TrimEnd(' ', ',');
                    //Fix 
                    argsString = argsString.Replace('.', '_');

                    //Each combination is a different TestCase
                    var withTagArgs = combination.Select(s => new CodeAttributeArgument(new CodePrimitiveExpression(s)))
                        .ToList()
                        .Concat(args)
                        .Concat(new[]
                        {
                            new CodeAttributeArgument("Category",
                                new CodePrimitiveExpression(string.Join(",", combination))),
                            new CodeAttributeArgument("TestName", new CodePrimitiveExpression(
                                $"{testMethod.Name} with {string.Join(",", combination)} and {argsString}"))
                        })
                        .ToArray();

                    _codeDomHelper.AddAttribute(testMethod, RowAttr, withTagArgs);
                }
            }
            else
            {
                _codeDomHelper.AddAttribute(testMethod, RowAttr, args.ToArray());
            }
        }

        public void SetTestClass(TestClassGenerationContext generationContext,
            string featureTitle, string featureDescription)
        {
            _codeDomHelper.AddAttribute(generationContext.TestClass, TestFixtureAttr);
            _codeDomHelper.AddAttribute(generationContext.TestClass, DescriptionAttr, featureTitle);
            generationContext.Namespace.Imports.Add(new CodeNamespaceImport("Unickq.SpecFlow.Selenium.Helpers"));
            generationContext.TestClass.Members.Add(new CodeMemberField("UnickqSpecFlowSeleniumGeneratorHelper",
                "helper"));
        }

        public void SetTestClassCategories(TestClassGenerationContext generationContext,
            IEnumerable<string> featureCategories)
        {
            _codeDomHelper.AddAttributeForEachValue(generationContext.TestClass, CategoryAttr, featureCategories);
        }

        public void SetTestClassCleanupMethod(TestClassGenerationContext generationContext)
        {
            generationContext.TestClassCleanupMethod.Statements.Insert(0, GenerateCodeSnippetStatement("helper.FeatureTearDown();"));
            _codeDomHelper.AddAttribute(generationContext.TestClassCleanupMethod, TestFixtureTearDownAttr);
        }

        public void SetTestClassIgnore(TestClassGenerationContext generationContext)
        {
            _codeDomHelper.AddAttribute(generationContext.TestClass, IgnoreAttr, "Test class is ignored\n");
        }

        public void SetTestClassParallelize(TestClassGenerationContext generationContext)
        {
            _codeDomHelper.AddAttribute(generationContext.TestClass, ParallelizableAttr);
        }

        public void SetTestClassInitializeMethod(
            TestClassGenerationContext generationContext)
        {
            _codeDomHelper.AddAttribute(generationContext.TestClassInitializeMethod, TestFixtureSetupAttr);
    
        }

        public void SetTestCleanupMethod(TestClassGenerationContext generationContext)
        {
            _codeDomHelper.AddAttribute(generationContext.TestCleanupMethod, TestTearDownAttr);
        }

        public void SetTestInitializeMethod(TestClassGenerationContext generationContext)
        {
            _codeDomHelper.AddAttribute(generationContext.TestInitializeMethod, TestSetupAttr);
            generationContext.TestClassInitializeMethod.Statements.Add(
                GenerateCodeSnippetStatement("helper = new UnickqSpecFlowSeleniumGeneratorHelper();"));
            generationContext.TestClassInitializeMethod.Statements.Add(
                GenerateCodeSnippetStatement("helper.FeatureSetup();"));
            generationContext.TestInitializeMethod.Statements.Add(GenerateCodeSnippetStatement("helper.SetUp();"));
        }

        public void SetTestMethod(TestClassGenerationContext generationContext,
            CodeMemberMethod testMethod, string scenarioTitle)
        {
            _codeDomHelper.AddAttribute(testMethod, TestAttr);
            _codeDomHelper.AddAttribute(testMethod, DescriptionAttr, scenarioTitle);
        }

        public void SetTestMethodIgnore(TestClassGenerationContext generationContext,
            CodeMemberMethod testMethod)
        {
            _codeDomHelper.AddAttribute(testMethod, IgnoreAttr, "Test scenario is ignored");
        }

        public void SetRowTest(TestClassGenerationContext generationContext,
            CodeMemberMethod testMethod, string scenarioTitle)
        {
            SetTestMethod(generationContext, testMethod, scenarioTitle);
        }

        public void SetTestMethodAsRow(TestClassGenerationContext generationContext,
            CodeMemberMethod testMethod, string scenarioTitle, string exampleSetName, string variantName,
            IEnumerable<KeyValuePair<string, string>> arguments)
        {
        }

        public void FinalizeTestClass(TestClassGenerationContext generationContext)
        {
            generationContext.TestCleanupMethod.Statements.RemoveAt(0);
            generationContext.TestCleanupMethod.Statements.Add(GenerateCodeSnippetStatement("helper.TearDown();"));

            foreach (var field in _fieldsToGenerate)
                if (!field.Equals("Browser", StringComparison.OrdinalIgnoreCase))
                {
                    generationContext.TestCleanupMethod.Statements.Add(
                        GenerateCodeSnippetStatement($"helper.ClearScenarioContext(testRunner.ScenarioContext, \"{field}\");"));
                }

            generationContext.TestCleanupMethod.Statements.Add(
                GenerateCodeSnippetStatement("testRunner.OnScenarioEnd();"));


            if (!_scenarioSetupMethodsAdded)
            {
                if (_hasBrowser)
                {
                    generationContext.ScenarioInitializeMethod.Statements.Add(
                        GenerateCodeSnippetStatement(
                            $"testRunner.ScenarioContext.Add(\"{Extensions.Driver}\", helper.Driver);"));
                }

                _scenarioSetupMethodsAdded = true;
            }
        }

        public UnitTestGeneratorTraits GetTraits()
        {
            return UnitTestGeneratorTraits.RowTests | UnitTestGeneratorTraits.ParallelExecution;
        }

        private CodeSnippetStatement GenerateCodeSnippetStatement(string str)
        {
            return new CodeSnippetStatement("            " + str);
        }

        private void GeneratePermutations(IReadOnlyList<List<string>> lists, ICollection<List<string>> result,
            int depth, List<string> current)
        {
            //TODO rajouter les CodePrimitiveExpression
            if (depth == lists.Count)
            {
                result.Add(current);
                return;
            }

            for (var i = 0; i < lists[depth].Count; i++)
            {
                var newList = new List<string>(current) {lists[depth][i]};
                GeneratePermutations(lists, result, depth + 1, newList);
            }
        }
    }
}