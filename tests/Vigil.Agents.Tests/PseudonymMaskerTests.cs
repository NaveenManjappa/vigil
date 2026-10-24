namespace Vigil.Agents.Tests;

public sealed class PseudonymMaskerTests
{
    [Fact]
    public void Masking_removes_every_registered_value_from_a_Rule_Hit_explanation()
    {
        var masker = new PseudonymMasker();
        masker.Register(IdentifierKind.DeviceId, "D-77");
        masker.Register(IdentifierKind.AccountId, "ACC-DEREK");
        masker.Register(IdentifierKind.AccountId, "ACC-PRIYA");

        var masked = masker.Mask("D-77 shared by ACC-DEREK, ACC-PRIYA");

        Assert.Equal("DEVICE-1 shared by ACCOUNT-1, ACCOUNT-2", masked);
    }

    [Fact]
    public void Masking_keeps_event_types_times_country_channel_amounts_and_Rule_Ids()
    {
        var masker = new PseudonymMasker();
        masker.Register(IdentifierKind.AccountId, "ACC-MARGARET");
        masker.Register(IdentifierKind.HolderId, "H-MARGARET");
        masker.Register(IdentifierKind.Email, "margaret@example.com");
        masker.Register(IdentifierKind.Phone, "+447700900123");
        masker.Register(IdentifierKind.BankAccount, "9f2c41");
        masker.Register(IdentifierKind.Address, "SW1A 1AA");
        masker.Register(IdentifierKind.IpAddress, "203.0.113.9");
        masker.Register(IdentifierKind.DeviceId, "D-77");

        var masked = masker.Mask(
            "2026-09-21T09:04:00Z BankDetailsChanged ACC-MARGARET (H-MARGARET) web GB 203.0.113.9 D-77 " +
            "bank 9f2c41 email margaret@example.com phone +447700900123 postcode SW1A 1AA amount 25000.00 R4");

        Assert.Equal(
            "2026-09-21T09:04:00Z BankDetailsChanged ACCOUNT-1 (HOLDER-1) web GB IP-1 DEVICE-1 " +
            "bank BANK-1 email EMAIL-1 phone PHONE-1 postcode ADDRESS-1 amount 25000.00 R4",
            masked);
    }

    [Fact]
    public void A_value_that_contains_a_shorter_registered_value_is_masked_whole()
    {
        var masker = new PseudonymMasker();
        masker.Register(IdentifierKind.AccountId, "ACC-PRI");
        masker.Register(IdentifierKind.AccountId, "ACC-PRIYA");

        var masked = masker.Mask("ACC-PRIYA and ACC-PRI");

        Assert.Equal("ACCOUNT-2 and ACCOUNT-1", masked);
    }

    [Fact]
    public void Unmasking_turns_pseudonyms_in_agent_text_back_into_real_identifiers()
    {
        var masker = new PseudonymMasker();
        for (var i = 1; i <= 10; i++)
            masker.Register(IdentifierKind.AccountId, $"ACC-{i:00}");

        var unmasked = masker.Unmask("ACCOUNT-10 shares a device with ACCOUNT-1.");

        Assert.Equal("ACC-10 shares a device with ACC-01.", unmasked);
    }

    [Fact]
    public void A_masker_rebuilt_from_the_stored_map_unmasks_the_stored_Finding()
    {
        var original = new PseudonymMasker();
        original.Register(IdentifierKind.AccountId, "ACC-MARGARET");
        original.Register(IdentifierKind.DeviceId, "D-77");
        var storedFinding = original.Mask("ACC-MARGARET logged in from D-77");

        var rebuilt = PseudonymMasker.FromMap(original.Map);

        Assert.Equal("ACCOUNT-1 logged in from DEVICE-1", storedFinding);
        Assert.Equal("ACC-MARGARET logged in from D-77", rebuilt.Unmask(storedFinding));
        Assert.Equal(new Dictionary<string, string> { ["ACCOUNT-1"] = "ACC-MARGARET", ["DEVICE-1"] = "D-77" }, rebuilt.Map);
    }

    [Fact]
    public void A_rebuilt_masker_keeps_existing_pseudonyms_and_numbers_new_values_after_them()
    {
        var original = new PseudonymMasker();
        original.Register(IdentifierKind.DeviceId, "D-77");
        original.Register(IdentifierKind.DeviceId, "D-78");

        var rebuilt = PseudonymMasker.FromMap(original.Map);

        Assert.Equal("DEVICE-2", rebuilt.Register(IdentifierKind.DeviceId, "D-78"));
        Assert.Equal("DEVICE-3", rebuilt.Register(IdentifierKind.DeviceId, "D-99"));
        Assert.Equal("ACCOUNT-1", rebuilt.Register(IdentifierKind.AccountId, "ACC-1"));
    }
}
