﻿// ------------------------------------------------------------------------------
//  <auto-generated>
//      This code was generated by SpecFlow (https://www.specflow.org/).
//      SpecFlow Version:3.9.0.0
//      SpecFlow Generator Version:3.9.0.0
// 
//      Changes to this file may cause incorrect behavior and will be lost if
//      the code is regenerated.
//  </auto-generated>
// ------------------------------------------------------------------------------
#region Designer generated code
#pragma warning disable
namespace FluentCMS.E2eTests.Features
{
    using TechTalk.SpecFlow;
    using System;
    using System.Linq;
    
    
    [System.CodeDom.Compiler.GeneratedCodeAttribute("TechTalk.SpecFlow", "3.9.0.0")]
    [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    [Xunit.TraitAttribute("Category", "RequiresFreshSetup")]
    [Xunit.TraitAttribute("Category", "RequiresAuthenticatedAdmin")]
    public partial class ContentTypeClientFeature : object, Xunit.IClassFixture<ContentTypeClientFeature.FixtureData>, System.IDisposable
    {
        
        private static TechTalk.SpecFlow.ITestRunner testRunner;
        
        private static string[] featureTags = new string[] {
                "RequiresFreshSetup",
                "RequiresAuthenticatedAdmin"};
        
        private Xunit.Abstractions.ITestOutputHelper _testOutputHelper;
        
#line 1 "ContentTypeClient.feature"
#line hidden
        
        public ContentTypeClientFeature(ContentTypeClientFeature.FixtureData fixtureData, FluentCMS_E2eTests_XUnitAssemblyFixture assemblyFixture, Xunit.Abstractions.ITestOutputHelper testOutputHelper)
        {
            this._testOutputHelper = testOutputHelper;
            this.TestInitialize();
        }
        
        public static void FeatureSetup()
        {
            testRunner = TechTalk.SpecFlow.TestRunnerManager.GetTestRunner();
            TechTalk.SpecFlow.FeatureInfo featureInfo = new TechTalk.SpecFlow.FeatureInfo(new System.Globalization.CultureInfo("en-US"), "Features", "Content Type Client", null, ProgrammingLanguage.CSharp, featureTags);
            testRunner.OnFeatureStart(featureInfo);
        }
        
        public static void FeatureTearDown()
        {
            testRunner.OnFeatureEnd();
            testRunner = null;
        }
        
        public void TestInitialize()
        {
        }
        
        public void TestTearDown()
        {
            testRunner.OnScenarioEnd();
        }
        
        public void ScenarioInitialize(TechTalk.SpecFlow.ScenarioInfo scenarioInfo)
        {
            testRunner.OnScenarioInitialize(scenarioInfo);
            testRunner.ScenarioContext.ScenarioContainer.RegisterInstanceAs<Xunit.Abstractions.ITestOutputHelper>(_testOutputHelper);
        }
        
        public void ScenarioStart()
        {
            testRunner.OnScenarioStart();
        }
        
        public void ScenarioCleanup()
        {
            testRunner.CollectScenarioErrors();
        }
        
        public virtual void FeatureBackground()
        {
#line 5
#line hidden
#line 6
 testRunner.Given("I have a \"ContentTypeClient\"", ((string)(null)), ((TechTalk.SpecFlow.Table)(null)), "Given ");
#line hidden
        }
        
        void System.IDisposable.Dispose()
        {
            this.TestTearDown();
        }
        
        [Xunit.SkippableFactAttribute(DisplayName="Get All")]
        [Xunit.TraitAttribute("FeatureTitle", "Content Type Client")]
        [Xunit.TraitAttribute("Description", "Get All")]
        public void GetAll()
        {
            string[] tagsOfScenario = ((string[])(null));
            System.Collections.Specialized.OrderedDictionary argumentsOfScenario = new System.Collections.Specialized.OrderedDictionary();
            TechTalk.SpecFlow.ScenarioInfo scenarioInfo = new TechTalk.SpecFlow.ScenarioInfo("Get All", null, tagsOfScenario, argumentsOfScenario, featureTags);
#line 8
this.ScenarioInitialize(scenarioInfo);
#line hidden
            if ((TagHelper.ContainsIgnoreTag(tagsOfScenario) || TagHelper.ContainsIgnoreTag(featureTags)))
            {
                testRunner.SkipScenario();
            }
            else
            {
                this.ScenarioStart();
#line 5
this.FeatureBackground();
#line hidden
#line 9
 testRunner.Given("I have 10 ContentTypes", ((string)(null)), ((TechTalk.SpecFlow.Table)(null)), "Given ");
#line hidden
#line 10
 testRunner.When("I get all content types", ((string)(null)), ((TechTalk.SpecFlow.Table)(null)), "When ");
#line hidden
#line 11
 testRunner.Then("I should see 10 content types", ((string)(null)), ((TechTalk.SpecFlow.Table)(null)), "Then ");
#line hidden
            }
            this.ScenarioCleanup();
        }
        
        [Xunit.SkippableFactAttribute(DisplayName="Create")]
        [Xunit.TraitAttribute("FeatureTitle", "Content Type Client")]
        [Xunit.TraitAttribute("Description", "Create")]
        public void Create()
        {
            string[] tagsOfScenario = ((string[])(null));
            System.Collections.Specialized.OrderedDictionary argumentsOfScenario = new System.Collections.Specialized.OrderedDictionary();
            TechTalk.SpecFlow.ScenarioInfo scenarioInfo = new TechTalk.SpecFlow.ScenarioInfo("Create", null, tagsOfScenario, argumentsOfScenario, featureTags);
#line 13
this.ScenarioInitialize(scenarioInfo);
#line hidden
            if ((TagHelper.ContainsIgnoreTag(tagsOfScenario) || TagHelper.ContainsIgnoreTag(featureTags)))
            {
                testRunner.SkipScenario();
            }
            else
            {
                this.ScenarioStart();
#line 5
this.FeatureBackground();
#line hidden
                TechTalk.SpecFlow.Table table4 = new TechTalk.SpecFlow.Table(new string[] {
                            "field",
                            "value"});
                table4.AddRow(new string[] {
                            "title",
                            "test"});
                table4.AddRow(new string[] {
                            "description",
                            "test description"});
                table4.AddRow(new string[] {
                            "slug",
                            "test-slug"});
#line 14
 testRunner.Given("I have a ContentTypeCreateRequest", ((string)(null)), table4, "Given ");
#line hidden
#line 19
 testRunner.When("I create a content type", ((string)(null)), ((TechTalk.SpecFlow.Table)(null)), "When ");
#line hidden
#line 20
 testRunner.Then("I should see the content type", ((string)(null)), ((TechTalk.SpecFlow.Table)(null)), "Then ");
#line hidden
            }
            this.ScenarioCleanup();
        }
        
        [Xunit.SkippableFactAttribute(DisplayName="Update")]
        [Xunit.TraitAttribute("FeatureTitle", "Content Type Client")]
        [Xunit.TraitAttribute("Description", "Update")]
        [Xunit.TraitAttribute("Category", "RequiresContentType")]
        public void Update()
        {
            string[] tagsOfScenario = new string[] {
                    "RequiresContentType"};
            System.Collections.Specialized.OrderedDictionary argumentsOfScenario = new System.Collections.Specialized.OrderedDictionary();
            TechTalk.SpecFlow.ScenarioInfo scenarioInfo = new TechTalk.SpecFlow.ScenarioInfo("Update", null, tagsOfScenario, argumentsOfScenario, featureTags);
#line 23
this.ScenarioInitialize(scenarioInfo);
#line hidden
            if ((TagHelper.ContainsIgnoreTag(tagsOfScenario) || TagHelper.ContainsIgnoreTag(featureTags)))
            {
                testRunner.SkipScenario();
            }
            else
            {
                this.ScenarioStart();
#line 5
this.FeatureBackground();
#line hidden
                TechTalk.SpecFlow.Table table5 = new TechTalk.SpecFlow.Table(new string[] {
                            "field",
                            "value"});
                table5.AddRow(new string[] {
                            "title",
                            "test-updated"});
                table5.AddRow(new string[] {
                            "description",
                            "test updated description"});
#line 24
 testRunner.Given("I have a ContentTypeUpdateRequest", ((string)(null)), table5, "Given ");
#line hidden
#line 28
 testRunner.When("I update a content type", ((string)(null)), ((TechTalk.SpecFlow.Table)(null)), "When ");
#line hidden
#line 29
 testRunner.Then("I should see the updated content type", ((string)(null)), ((TechTalk.SpecFlow.Table)(null)), "Then ");
#line hidden
            }
            this.ScenarioCleanup();
        }
        
        [Xunit.SkippableFactAttribute(DisplayName="Delete")]
        [Xunit.TraitAttribute("FeatureTitle", "Content Type Client")]
        [Xunit.TraitAttribute("Description", "Delete")]
        [Xunit.TraitAttribute("Category", "RequiresContentType")]
        public void Delete()
        {
            string[] tagsOfScenario = new string[] {
                    "RequiresContentType"};
            System.Collections.Specialized.OrderedDictionary argumentsOfScenario = new System.Collections.Specialized.OrderedDictionary();
            TechTalk.SpecFlow.ScenarioInfo scenarioInfo = new TechTalk.SpecFlow.ScenarioInfo("Delete", null, tagsOfScenario, argumentsOfScenario, featureTags);
#line 32
this.ScenarioInitialize(scenarioInfo);
#line hidden
            if ((TagHelper.ContainsIgnoreTag(tagsOfScenario) || TagHelper.ContainsIgnoreTag(featureTags)))
            {
                testRunner.SkipScenario();
            }
            else
            {
                this.ScenarioStart();
#line 5
this.FeatureBackground();
#line hidden
#line 33
 testRunner.When("I delete a content type", ((string)(null)), ((TechTalk.SpecFlow.Table)(null)), "When ");
#line hidden
#line 34
 testRunner.Then("I should not see the content type", ((string)(null)), ((TechTalk.SpecFlow.Table)(null)), "Then ");
#line hidden
            }
            this.ScenarioCleanup();
        }
        
        [Xunit.SkippableFactAttribute(DisplayName="Set Field")]
        [Xunit.TraitAttribute("FeatureTitle", "Content Type Client")]
        [Xunit.TraitAttribute("Description", "Set Field")]
        [Xunit.TraitAttribute("Category", "RequiresContentType")]
        public void SetField()
        {
            string[] tagsOfScenario = new string[] {
                    "RequiresContentType"};
            System.Collections.Specialized.OrderedDictionary argumentsOfScenario = new System.Collections.Specialized.OrderedDictionary();
            TechTalk.SpecFlow.ScenarioInfo scenarioInfo = new TechTalk.SpecFlow.ScenarioInfo("Set Field", null, tagsOfScenario, argumentsOfScenario, featureTags);
#line 37
this.ScenarioInitialize(scenarioInfo);
#line hidden
            if ((TagHelper.ContainsIgnoreTag(tagsOfScenario) || TagHelper.ContainsIgnoreTag(featureTags)))
            {
                testRunner.SkipScenario();
            }
            else
            {
                this.ScenarioStart();
#line 5
this.FeatureBackground();
#line hidden
#line 38
 testRunner.Given("I have a ContentType", ((string)(null)), ((TechTalk.SpecFlow.Table)(null)), "Given ");
#line hidden
                TechTalk.SpecFlow.Table table6 = new TechTalk.SpecFlow.Table(new string[] {
                            "field",
                            "value"});
                table6.AddRow(new string[] {
                            "slug",
                            "dummy-field-slug"});
                table6.AddRow(new string[] {
                            "title",
                            "dummy-field-title"});
                table6.AddRow(new string[] {
                            "description",
                            "dummy-field-description"});
                table6.AddRow(new string[] {
                            "label",
                            "dummy-field-label"});
                table6.AddRow(new string[] {
                            "placeholder",
                            "dummy-field-placeholder"});
                table6.AddRow(new string[] {
                            "hint",
                            "dummy-field-hint"});
                table6.AddRow(new string[] {
                            "defaultValue",
                            "dummy-field-defaultValue"});
                table6.AddRow(new string[] {
                            "isRequired",
                            "false"});
                table6.AddRow(new string[] {
                            "isPrivate",
                            "false"});
                table6.AddRow(new string[] {
                            "fieldType",
                            "dummy-field-fieldType"});
                table6.AddRow(new string[] {
                            "metadata.field1",
                            "dummy-field-metadata-field1"});
                table6.AddRow(new string[] {
                            "metadata.field2",
                            "dummy-field-metadata-field2"});
                table6.AddRow(new string[] {
                            "metadata.field3",
                            "dummy-field-metadata-field3"});
                table6.AddRow(new string[] {
                            "metadata.field4",
                            "dummy-field-metadata-field4"});
#line 39
 testRunner.When("I set a field", ((string)(null)), table6, "When ");
#line hidden
#line 56
 testRunner.Then("I should see the field", ((string)(null)), ((TechTalk.SpecFlow.Table)(null)), "Then ");
#line hidden
            }
            this.ScenarioCleanup();
        }
        
        [Xunit.SkippableFactAttribute(DisplayName="Delete Field")]
        [Xunit.TraitAttribute("FeatureTitle", "Content Type Client")]
        [Xunit.TraitAttribute("Description", "Delete Field")]
        [Xunit.TraitAttribute("Category", "RequiresContentType")]
        [Xunit.TraitAttribute("Category", "RequiresContentTypeSetField")]
        public void DeleteField()
        {
            string[] tagsOfScenario = new string[] {
                    "RequiresContentType",
                    "RequiresContentTypeSetField"};
            System.Collections.Specialized.OrderedDictionary argumentsOfScenario = new System.Collections.Specialized.OrderedDictionary();
            TechTalk.SpecFlow.ScenarioInfo scenarioInfo = new TechTalk.SpecFlow.ScenarioInfo("Delete Field", null, tagsOfScenario, argumentsOfScenario, featureTags);
#line 60
this.ScenarioInitialize(scenarioInfo);
#line hidden
            if ((TagHelper.ContainsIgnoreTag(tagsOfScenario) || TagHelper.ContainsIgnoreTag(featureTags)))
            {
                testRunner.SkipScenario();
            }
            else
            {
                this.ScenarioStart();
#line 5
this.FeatureBackground();
#line hidden
#line 61
 testRunner.Given("I have a ContentType", ((string)(null)), ((TechTalk.SpecFlow.Table)(null)), "Given ");
#line hidden
#line 62
 testRunner.When("I delete a field", ((string)(null)), ((TechTalk.SpecFlow.Table)(null)), "When ");
#line hidden
#line 63
 testRunner.Then("Wait 1 second", ((string)(null)), ((TechTalk.SpecFlow.Table)(null)), "Then ");
#line hidden
#line 64
 testRunner.Then("I should not see the field", ((string)(null)), ((TechTalk.SpecFlow.Table)(null)), "Then ");
#line hidden
            }
            this.ScenarioCleanup();
        }
        
        [System.CodeDom.Compiler.GeneratedCodeAttribute("TechTalk.SpecFlow", "3.9.0.0")]
        [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
        public class FixtureData : System.IDisposable
        {
            
            public FixtureData()
            {
                ContentTypeClientFeature.FeatureSetup();
            }
            
            void System.IDisposable.Dispose()
            {
                ContentTypeClientFeature.FeatureTearDown();
            }
        }
    }
}
#pragma warning restore
#endregion
