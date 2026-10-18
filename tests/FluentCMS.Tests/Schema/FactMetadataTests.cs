using FluentCMS.Core.Schema;

namespace FluentCMS.Tests.Schema;

// The names are a contract with the schema document (SCH-06) and entity values (F-ENT), so a rename has to be deliberate.
public sealed class FactMetadataTests
{
    [Fact]
    public void Every_fact_carries_a_source_an_owner_and_its_validity()
    {
        Assert.Equal(["source", "owner", "validFrom", "validUntil", "lastVerified"], FactMetadata.Properties);
        Assert.Equal("value", FactMetadata.Value);
    }

    [Fact]
    public void A_source_is_a_url_a_file_a_person_or_a_system()
    {
        Assert.Equal(["url", "file", "person", "system"], FactMetadata.SourceKinds);
        Assert.Equal(("kind", "reference"), (FactMetadata.SourceKind, FactMetadata.SourceReference));
    }
}
