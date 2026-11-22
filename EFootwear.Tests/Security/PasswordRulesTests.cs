using EFootwear.Domain.Common;
using EFootwear.Domain.Security;

namespace EFootwear.Tests.Security;

/// <summary>Docx 1.5: a new password has at least 8 characters, letters and digits, and differs from the current one.</summary>
public class PasswordRulesTests
{
    [Theory]
    [InlineData("Password1")]
    [InlineData("abcdefg1")]
    [InlineData("12345678a")]
    [InlineData("Temporary#1234")]
    public void A_password_with_enough_characters_letters_and_digits_is_accepted(string password) =>
        PasswordRules.EnsureValid(password);

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Ab1")]
    [InlineData("Abcdef1")]
    public void A_password_shorter_than_the_minimum_is_refused(string? password) =>
        Assert.Equal(ErrorKind.Validation, Assert.Throws<DomainException>(() => PasswordRules.EnsureValid(password)).Kind);

    [Theory]
    [InlineData("onlyletters")]
    [InlineData("1234567890")]
    [InlineData("########")]
    public void A_password_without_both_letters_and_digits_is_refused(string password)
    {
        var exception = Assert.Throws<DomainException>(() => PasswordRules.EnsureValid(password));
        Assert.Equal(ErrorKind.Validation, exception.Kind);
        Assert.Contains("letters and digits", exception.Message);
    }

    [Fact]
    public void A_password_equal_to_the_current_one_is_refused() =>
        Assert.Equal(ErrorKind.Validation,
            Assert.Throws<DomainException>(() => PasswordRules.EnsureValid("Password1", currentPassword: "Password1")).Kind);

    [Fact]
    public void A_password_longer_than_the_maximum_is_refused() =>
        Assert.Throws<DomainException>(() => PasswordRules.EnsureValid(new string('a', PasswordRules.MaximumLength) + "1x"));

    [Fact]
    public void The_message_names_the_subject_it_was_given() =>
        Assert.Contains("Temporary password",
            Assert.Throws<DomainException>(() => PasswordRules.EnsureValid("x", subjectName: "Temporary password")).Message);
}
