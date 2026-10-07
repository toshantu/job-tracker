using JobTracker.Api.Ai;
using Xunit;

namespace JobTracker.Api.Tests;

public class CvRedactorTests
{
    [Theory]
    [InlineData("alex.example@example.com")]
    [InlineData("first.last+jobs@sub.example.co.uk")]
    [InlineData("a_b-c@example.org")]
    public void Removes_email_addresses(string email)
    {
        var result = CvRedactor.Redact($"Write to {email} please.");

        Assert.Equal("Write to [email removed] please.", result.Text);
        Assert.Equal(1, result.EmailsRemoved);
        Assert.Equal(0, result.PhonesRemoved);
        Assert.Equal(0, result.LinksRemoved);
    }

    [Theory]
    [InlineData("https://www.linkedin.com/in/alex-example")]
    [InlineData("http://example.com/portfolio?id=123")]
    [InlineData("www.example.com/about")]
    [InlineData("github.com/alex-example")]
    [InlineData("linkedin.com/in/alex-example")]
    public void Removes_links(string link)
    {
        var result = CvRedactor.Redact($"See {link} for more.");

        Assert.Equal("See [link removed] for more.", result.Text);
        Assert.Equal(1, result.LinksRemoved);
        Assert.Equal(0, result.EmailsRemoved);
        Assert.Equal(0, result.PhonesRemoved);
    }

    [Theory]
    [InlineData("+44 7700 900123")]
    [InlineData("07700 900123")]
    [InlineData("+91 98765 43210")]
    [InlineData("98765-43210")]
    [InlineData("(415) 555-0132")]
    [InlineData("415-555-0132")]
    [InlineData("415.555.0132")]
    [InlineData("+1 415 555 0132")]
    [InlineData("4155550132")]
    public void Removes_phone_numbers(string phone)
    {
        var result = CvRedactor.Redact($"Call {phone} any time.");

        Assert.Equal("Call [phone removed] any time.", result.Text);
        Assert.Equal(1, result.PhonesRemoved);
        Assert.Equal(0, result.EmailsRemoved);
        Assert.Equal(0, result.LinksRemoved);
    }

    [Theory]
    [InlineData("2019-2023")]
    [InlineData("2019 \u2013 2023")]
    [InlineData("Jan 2020 \u2013 Mar 2023")]
    [InlineData("Built with C# 12 on .NET 8 and ASP.NET Core.")]
    [InlineData("Upgraded Python 3.11 services from v2.4.1 to v3.2.1.")]
    [InlineData("Runs on version 10.0.12 and build 10.0.12.4567.")]
    [InlineData("ISO 27001 certified, SQL Server 2019, PostgreSQL 15.")]
    [InlineData("99.9% uptime for 120,000 users and $1,200,000 saved.")]
    [InlineData("Node.js 20, Vue.js 3 and Next.js 15.")]
    [InlineData("Certified in 2019 2021 2022")]
    [InlineData("Led 12 engineers across 3 sites between 2018-2021.")]
    [InlineData("Closed ticket ABC123456789 within the SLA.")]
    [InlineData("Released on 12-05-2021 and again on 2021-05-12.")]
    [InlineData("Order dates 2020-03-15 2021-04-20")]
    [InlineData("Version 1.2.3.4.5.6.7.8.9 on 192.168.100.200")]
    public void Keeps_dates_versions_and_technology_names(string text)
    {
        var result = CvRedactor.Redact(text);

        Assert.Equal(text, result.Text);
        Assert.Equal(0, result.EmailsRemoved);
        Assert.Equal(0, result.PhonesRemoved);
        Assert.Equal(0, result.LinksRemoved);
    }

    [Fact]
    public void Does_not_merge_a_phone_number_with_the_numbers_around_it()
    {
        // Numbers on the next line, or after a double space, are not part of the phone number.
        Assert.Equal("Phone [phone removed]\n2019-2023", CvRedactor.Redact("Phone 415 555 0132\n2019-2023").Text);
        Assert.Equal("Phone: [phone removed]  2019", CvRedactor.Redact("Phone: 415 555 0132  2019").Text);
    }

    [Fact]
    public void Counts_what_it_removed()
    {
        var result = CvRedactor.Redact("alex@example.com | +44 7700 900123 | https://a.example/x | github.com/alex");

        Assert.Equal("[email removed] | [phone removed] | [link removed] | [link removed]", result.Text);
        Assert.Equal(1, result.EmailsRemoved);
        Assert.Equal(1, result.PhonesRemoved);
        Assert.Equal(2, result.LinksRemoved);
    }

    [Fact]
    public void Returns_empty_text_for_empty_input()
    {
        Assert.Equal(string.Empty, CvRedactor.Redact(null).Text);
        Assert.Equal(string.Empty, CvRedactor.Redact(string.Empty).Text);
    }

    [Fact]
    public void Cleans_the_synthetic_cv_without_touching_its_content()
    {
        var result = CvRedactor.Redact(SyntheticTexts.Cv);

        Assert.DoesNotContain(SyntheticTexts.FakeEmail, result.Text);
        Assert.DoesNotContain("7700 900123", result.Text);
        Assert.DoesNotContain("linkedin.com/in/alex-example", result.Text);
        Assert.DoesNotContain("github.com/alex-example", result.Text);

        Assert.Contains("C# 12", result.Text);
        Assert.Contains(".NET 8", result.Text);
        Assert.Contains("ASP.NET Core 8", result.Text);
        Assert.Contains("v2.4.1 to v3.2.1", result.Text);
        Assert.Contains("Jan 2020 - Mar 2023", result.Text);
        Assert.Contains("(2019 - 2020)", result.Text);
        Assert.Contains("120,000 users with 99.9% uptime", result.Text);

        Assert.Equal(1, result.EmailsRemoved);
        Assert.Equal(1, result.PhonesRemoved);
        Assert.Equal(2, result.LinksRemoved);
    }
}
