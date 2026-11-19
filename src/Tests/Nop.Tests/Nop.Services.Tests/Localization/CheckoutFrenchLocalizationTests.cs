using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;
using NUnit.Framework;

namespace Nop.Tests.Nop.Services.Tests.Localization;

[TestFixture]
public partial class CheckoutFrenchLocalizationTests
{
    private static readonly string EnglishResourcesPath = FindResourceFile(
        "src", "Presentation", "Nop.Web", "App_Data", "Localization", "defaultResources.nopres.xml");

    private static readonly string FrenchResourcesPath = FindResourceFile(
        "src", "Presentation", "Nop.Web", "App_Data", "Localization", "fr-FR", "checkout.xml");

    private static readonly HashSet<string> CheckoutReferencedKeys =
    [
        "Address.Fields.Email",
        "Address.Fields.FaxNumber",
        "Address.Fields.PhoneNumber",
        "Checkout",
        "Checkout.Address.NotFound",
        "Checkout.Addresses.Invalid",
        "Checkout.BillingAddress",
        "Checkout.BillToThisAddress",
        "Checkout.Button",
        "Checkout.ConfirmButton",
        "Checkout.ConfirmOrder",
        "Checkout.ConfirmYourOrder",
        "Checkout.Disabled",
        "Checkout.EditAddress",
        "Checkout.EnterBillingAddress",
        "Checkout.EnterShippingAddress",
        "Checkout.MinOrderPlacementInterval",
        "Checkout.MinOrderSubtotalAmount",
        "Checkout.MinOrderTotalAmount",
        "Checkout.NewAddress",
        "Checkout.NextButton",
        "Checkout.NoPaymentMethods",
        "Checkout.OrderNumber",
        "Checkout.OrderSummary",
        "Checkout.OrEnterNewAddress",
        "Checkout.PaymentError",
        "Checkout.PaymentInfo",
        "Checkout.PaymentMethod",
        "Checkout.PickupPoints",
        "Checkout.PickupPoints.Description",
        "Checkout.PickupPoints.Name",
        "Checkout.PickupPoints.NotAvailable",
        "Checkout.PickupPoints.NullName",
        "Checkout.PickupPoints.SelectPickupPoint",
        "Checkout.PlacedOrderDetails",
        "Checkout.Progress.Address",
        "Checkout.Progress.Cart",
        "Checkout.Progress.Complete",
        "Checkout.Progress.Confirm",
        "Checkout.Progress.Payment",
        "Checkout.Progress.Shipping",
        "Checkout.SelectBillingAddress",
        "Checkout.SelectBillingAddressOrEnterNewOne",
        "Checkout.SelectDesiredDeliveryDate",
        "Checkout.SelectPaymentMethod",
        "Checkout.SelectPaymentMethod.MethodAndFee",
        "Checkout.SelectShippingAddress",
        "Checkout.SelectShippingAddressOrEnterNewOne",
        "Checkout.SelectShippingMethod",
        "Checkout.SelectShippingMethod.MethodAndFee",
        "Checkout.ShippingAddress",
        "Checkout.ShippingIsNotAllowed",
        "Checkout.ShippingMethod",
        "Checkout.ShippingMethod.ShippingFromMultipleLocations",
        "Checkout.ShippingOptionCouldNotBeLoaded",
        "Checkout.ShipToSameAddress",
        "Checkout.ShipToThisAddress",
        "Checkout.SubmittingOrder",
        "Checkout.TermsOfService",
        "Checkout.TermsOfService.IAccept",
        "Checkout.TermsOfService.PleaseAccept",
        "Checkout.TermsOfService.Read",
        "Checkout.ThankYou",
        "Checkout.ThankYou.Continue",
        "Checkout.UseRewardPoints",
        "Checkout.VatNumber",
        "Checkout.VatNumber.Disabled",
        "Checkout.VatNumber.Warning",
        "Checkout.YourOrderHasBeenSuccessfullyProcessed",
        "Common.Back",
        "Common.Cancel",
        "Common.Continue",
        "Common.Delete",
        "Common.Edit",
        "Common.FileUploader.Browse",
        "Common.FileUploader.DropFiles",
        "Common.FileUploader.Processing",
        "Common.LoadingNextStep",
        "Common.Save",
        "Common.WrongCaptchaMessage",
        "PageTitle.Checkout",
        "PaymentMethod.NotAvailableMethodsError",
        "PaymentMethod.SpecifyMethodError",
        "ShippingMethod.NotAvailableMethodsError",
        "ShippingMethod.SpecifyMethodError"
    ];

    [GeneratedRegex(@"\{(\d+)\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderRegex();

    [Test]
    public void FrenchCheckoutResourcesShouldCoverAllEnglishCheckoutKeys()
    {
        var englishResources = LoadLocaleResources(NormalizePath(EnglishResourcesPath));
        var frenchResources = LoadLocaleResources(NormalizePath(FrenchResourcesPath));

        var missingKeys = CheckoutReferencedKeys
            .Where(key => !frenchResources.ContainsKey(key))
            .OrderBy(key => key)
            .ToList();

        missingKeys.Should().BeEmpty(
            because: "every checkout English key must have a French equivalent in fr-FR/checkout.xml");

        var emptyFrenchValues = CheckoutReferencedKeys
            .Where(key => frenchResources.TryGetValue(key, out var value) && string.IsNullOrWhiteSpace(value))
            .OrderBy(key => key)
            .ToList();

        emptyFrenchValues.Should().BeEmpty(
            because: "French checkout translations must not be empty");

        var duplicateFrenchKeys = frenchResources
            .GroupBy(resource => resource.Key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        duplicateFrenchKeys.Should().BeEmpty(
            because: "French checkout resource keys must be unique");

        var placeholderMismatches = CheckoutReferencedKeys
            .Where(key => englishResources.ContainsKey(key) && frenchResources.ContainsKey(key))
            .Select(key => new
            {
                Key = key,
                EnglishPlaceholders = ExtractPlaceholders(englishResources[key]),
                FrenchPlaceholders = ExtractPlaceholders(frenchResources[key])
            })
            .Where(item => !item.EnglishPlaceholders.SequenceEqual(item.FrenchPlaceholders))
            .Select(item => $"{item.Key}: expected [{string.Join(", ", item.EnglishPlaceholders)}], got [{string.Join(", ", item.FrenchPlaceholders)}]")
            .ToList();

        placeholderMismatches.Should().BeEmpty(
            because: "French checkout translations must preserve placeholder tokens from English");
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path);

    private static string FindResourceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate resource file: {string.Join(Path.DirectorySeparatorChar, relativeParts)}");
    }

    private static Dictionary<string, string> LoadLocaleResources(string filePath)
    {
        var document = XDocument.Load(filePath);

        return document
            .Descendants("LocaleResource")
            .Select(element => new
            {
                Name = element.Attribute("Name")?.Value,
                Value = element.Element("Value")?.Value
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToDictionary(item => item.Name!, item => item.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ExtractPlaceholders(string value) =>
        PlaceholderRegex()
            .Matches(value)
            .Select(match => match.Value)
            .OrderBy(placeholder => placeholder)
            .ToList();
}
