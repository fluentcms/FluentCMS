using FluentCMS.E2eTests.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TechTalk.SpecFlow.Assist;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Given("I have Credentials")]
    public void GivenIHaveCredentials(Table table)
    {
        var credentials = table.CreateInstance<Credential>();
        context.Set(credentials);
    }
}
