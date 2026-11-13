using FluentCMS.Publishing;
using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests.Publishing;

public class PublishPolicyOptionsTests
{
    [Fact]
    public void WhatTheOptionsDoNotSetIsTheDefaultOfTheAttribute()
    {
        var options = new PublishPolicyOptions();
        var attribute = new PublishPolicyAttribute();

        options.UnpublishedReferences.ShouldBe(attribute.UnpublishedReferences);
        options.MaxVersions.ShouldBe(attribute.MaxVersions);
        options.TrashRetentionDays.ShouldBe(attribute.TrashRetentionDays);
        (options.UnpublishedReferences, options.MaxVersions, options.TrashRetentionDays).ShouldBe((UnpublishedReferencePolicy.Warn, 50, 30));
    }
}
