using System;
using CMS.Ecommerce.Web.UI;
using CMS.Helpers;
using System.Web;
using CMS.DataEngine;


public partial class NovalnetGatewayForm : CMSPaymentGatewayForm
{
    /// <summary>
    /// Called automatically after the related payment provider is assigned to the form and initialized.
    /// </summary>
    protected override void OnInitialized()
    {
        // Attempts to get a default email address value from the Kentico customer object

        string currentUrl = HttpContext.Current.Request.Url.AbsoluteUri.ToString();
        if (currentUrl.Contains("novalnet_error_message="))
        {
            string[] parts = currentUrl.Split(new char[] { '?', '&' });
            lblError.Text = Server.UrlDecode(parts[2].Replace("novalnet_error_message=", ""));
            lblError.Visible = true;         
        }
        else
        {
            string shopTestMode = SettingsKeyInfoProvider.GetValue("test_mode");
            if (shopTestMode == "True")
            {
                string testModeText = ResHelper.GetString("custom.testmode_notification");
                testmode.Visible = true;
                testmode.Text = testModeText;

            }
            string localizedResult = ResHelper.GetString("custom.payment_description");
            nnDescription.Text = localizedResult;
        }
        
    }

    /// <summary>
    /// Used for validation of the payment form's inputs.
    /// </summary>
    public override string ValidateData()
    {
        // The 'ValidateData' method of the base class delegates the validation logic to the related payment provider
        // First gets the form's data using the 'GetPaymentGatewayData()' method
        // and then validates it by calling the provider's 'ValidateCustomData' method
        string error = base.ValidateData();

        if (!string.IsNullOrEmpty(error))
        {
            lblError.Visible = true;
            lblError.Text = error;
        }

        return error;
    }

    /// <summary>
    /// Redirect to continue shopping page
    /// </summary>
    protected void continueShoppingEvent(object sender, EventArgs e)
    {
        URLHelper.Redirect(ResolveUrl("~/default.aspx"));
    }
}