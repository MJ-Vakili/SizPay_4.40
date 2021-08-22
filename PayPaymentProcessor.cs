//http://nopfarsi.ir
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Core.Infrastructure;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nop.Services.Tax;

namespace NopFarsi.Payment.SizPay
{
    /// <summary>
    /// PayStandard payment processor
    /// </summary>
    public class PayPaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields

        private readonly CurrencySettings _currencySettings;
        private readonly ICheckoutAttributeParser _checkoutAttributeParser;
        private readonly ICurrencyService _currencyService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILocalizationService _localizationService;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly ISettingService _settingService;
        private readonly ITaxService _taxService;
        private readonly IWebHelper _webHelper;
        private readonly IPaymentService paymentService;
        private readonly PayPaymentSettings _payPaymentSettings;
        private readonly ILogger logger;

        #endregion Fields

        #region Ctor

        public PayPaymentProcessor(CurrencySettings currencySettings,
            ICheckoutAttributeParser checkoutAttributeParser,
            ICurrencyService currencyService,
            IGenericAttributeService genericAttributeService,
            IHttpContextAccessor httpContextAccessor,
            ILocalizationService localizationService,
            IOrderTotalCalculationService orderTotalCalculationService,
            ISettingService settingService,
            ITaxService taxService,
            IWebHelper webHelper,
            PayPaymentSettings payPaymentSettings,
            ILogger logger,
            IPaymentService paymentService)
        {
            this._currencySettings = currencySettings;
            this._checkoutAttributeParser = checkoutAttributeParser;
            this._currencyService = currencyService;
            this._genericAttributeService = genericAttributeService;
            this._httpContextAccessor = httpContextAccessor;
            this._localizationService = localizationService;
            this._orderTotalCalculationService = orderTotalCalculationService;
            this._settingService = settingService;
            this._taxService = taxService;
            this._webHelper = webHelper;
            this._payPaymentSettings = payPaymentSettings;
            this.logger = logger;
            this.paymentService = paymentService;
        }

        #endregion Ctor

        #region Utilities

        /// <summary>
        /// Gets Pay URL
        /// </summary>
        /// <returns></returns>
        private string GetPayUrl()
        {
            return "https://sizpay.ir/payment/send";
        }

        /// <summary>
        /// Gets IPN Pay URL
        /// </summary>
        /// <returns></returns>
        private string GetIpnPayUrl()
        {
            return "https://sizpay.ir/payment/verify";
        }

        #endregion Utilities

        #region Methods

        /// <summary>
        /// Gets a configuration page URL
        /// </summary>
        public override string GetConfigurationPageUrl()
        {

            return $"{_webHelper.GetStoreLocation()}Admin/SizPay/Configure";
        }

        /// <summary>
        /// Gets a name of a view component for displaying plugin in public store ("payment info" checkout step)
        /// </summary>
        /// <returns>View component name</returns>
        public string GetPublicViewComponentName()
        {
            return "PaymentPay";
        }

        /// <summary>
        /// Install the plugin
        /// </summary>
        public override async Task InstallAsync()
        {
            await base.InstallAsync();
            await this._localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Pay.Fields.UserName", "Key");
            await this._localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Pay.Fields.Password", "IV");
            await this._localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Pay.Fields.MerchantID", "Merchant Id");
            await this._localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Pay.Fields.TerminalID", "Terminal Id");
            await this._localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Pay.Fields.IsToman", "واحد پول فروشگاه تومان است");
            await this._localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.SizPay.Fields.RedirectionTip", "به درگاه سیزپی منتقل می شوید.");
        }

        /// <summary>
        /// Uninstall the plugin
        /// </summary>
        public override async Task UninstallAsync()
        {
            //settings
            await _settingService.DeleteSettingAsync<PayPaymentSettings>();

            //locales
            await _localizationService.DeleteLocaleResourceAsync("plugins.payments.pay.Instructions");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Pay.Fields.Api");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Pay.Fields.Api.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Pay.Fields.Redirect");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Pay.Fields.Redirect.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Pay.Fields.AdditionalFee");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Pay.Fields.AdditionalFee.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Pay.Fields.AdditionalFeePercentage");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Pay.Fields.AdditionalFeePercentage.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Pay.Fields.RedirectionTip");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Pay.PaymentMethodDescription");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Pay.RoundingWarning");

            await base.UninstallAsync();
        }

        public Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            return Task.FromResult(new ProcessPaymentResult());
        }

        public async Task PostProcessPaymentAsync(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            //create common query parameters for the request
            try
            {
                ServicePointManager.ServerCertificateValidationCallback =
                    delegate (object s, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) { return true; };
                var kimial = new KimialPG.KimiaIPGRouteServiceSoapClient(KimialPG.KimiaIPGRouteServiceSoapClient.EndpointConfiguration.KimiaIPGRouteServiceSoap);
                decimal OrderTotal = postProcessPaymentRequest.Order.OrderTotal;
                if (_payPaymentSettings.IsToman)
                    OrderTotal = postProcessPaymentRequest.Order.OrderTotal * 10;
                string returnUrl = this._webHelper.GetStoreLocation() + "SizPay/Verify?OrderId=" + postProcessPaymentRequest.Order.Id.ToString();

                DateTime d = DateTime.Now;
                PersianCalendar pc = new PersianCalendar();

                var result1 = await kimial.GetTokenAsync(new KimialPG.GenerateToken()
                {
                    UserName = _payPaymentSettings.UserName,
                    Password = _payPaymentSettings.Password,
                    MerchantID = _payPaymentSettings.MerchentId,
                    TerminalID = _payPaymentSettings.TerminalId,
                    Amount = Convert.ToInt32(OrderTotal),
                    DocDate = string.Format("{0}/{1}/{2}", pc.GetYear(d), pc.GetMonth(d), pc.GetDayOfMonth(d)),
                    OrderID = postProcessPaymentRequest.Order.Id.ToString(),
                    ReturnURL = returnUrl,
                    //ExtraInf=_payPaymentSettings.InvoiceNo,
                    InvoiceNo = postProcessPaymentRequest.Order.Id.ToString(),
                    AppExtraInf = new KimialPG.AppExtraInf(),
                });//.Body.GetTokenResult;
                var result = result1.Body.GetTokenResult;
                if (result.ResCod == 0)
                {
                    postProcessPaymentRequest.Order.AuthorizationTransactionCode = result.Token;
                    await EngineContext.Current.Resolve<IOrderService>().UpdateOrderAsync(postProcessPaymentRequest.Order);

                    this._httpContextAccessor.HttpContext.Response.Redirect(
                        this._webHelper.GetStoreLocation() + "SizPay/Pay?MerchantID=" + _payPaymentSettings.MerchentId
                        + "&TerminalID=" + _payPaymentSettings.TerminalId +
                        "&Token=" + result.Token);
                }
                else
                {
                    await this.logger.ErrorAsync("Error Code : " + result.ResCod + " " + "Error Message : " + result.Message);
                    this._httpContextAccessor.HttpContext.Response.Redirect(
                        this._webHelper.GetStoreLocation() + "SizPay/Error?ErrorCode=" + result.ResCod
                        + "&Message=" + System.Net.WebUtility.UrlEncode(result.Message));
                }
            }
            catch (Exception exp)
            {
                await this.logger.ErrorAsync("Error Code : " + exp.Message);
                throw new NopException("Error Message" + exp.Message);
            }
        }

        public Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
        {
            return Task.FromResult(false);
        }

        public async Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart)
        {
            return await paymentService.CalculateAdditionalFeeAsync(cart, 0, false);
        }

        public Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest capturePaymentRequest)
        {
            CapturePaymentResult capture = new CapturePaymentResult();
            capture.AddError("Capture method not supported.");
            return Task.FromResult(capture);
        }

        public Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest refundPaymentRequest)
        {
            RefundPaymentResult refund = new RefundPaymentResult();
            refund.AddError("Refund method not supported.");
            return Task.FromResult(refund);
        }

        public Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest voidPaymentRequest)
        {
            VoidPaymentResult voidPay = new VoidPaymentResult();
            voidPay.AddError("Void method not supported.");
            return Task.FromResult(voidPay);
        }

        public Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            ProcessPaymentResult p = new ProcessPaymentResult();
            p.AddError("Recurring payment not supported.");
            return Task.FromResult(p);
        }

        public Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            CancelRecurringPaymentResult c = new CancelRecurringPaymentResult();
            c.AddError("Recurring payment not supported.");
            return Task.FromResult(c);
        }

        public Task<bool> CanRePostProcessPaymentAsync(Order order)
        {
            if (order == null)
            {
                throw new ArgumentNullException("order");
            }
            return Task.FromResult((DateTime.UtcNow - order.CreatedOnUtc).TotalSeconds >= 5.0);
        }

        public Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form)
        {
            if (form == null)
            {
                throw new ArgumentException(nameof(form));
            }

            //try to get errors
            if (form.TryGetValue("Errors", out StringValues errorsString) && !StringValues.IsNullOrEmpty(errorsString))
            {
                var errorList = new List<string> { errorsString.ToString() };
                return Task.FromResult<IList<string>>(errorList);
            }

            return Task.FromResult<IList<string>>(new List<string>());
        }

        public Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form)
        {
            return Task.FromResult(new ProcessPaymentRequest());
        }

        public Task<string> GetPaymentMethodDescriptionAsync()
        {
            return Task.FromResult(_localizationService.GetResourceAsync("Plugins.Payments.SizPay.Fields.RedirectionTip").Result);
        }

        #endregion Methods

        #region Properties

        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType
        {
            get { return RecurringPaymentType.NotSupported; }
        }

        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType
        {
            get { return PaymentMethodType.Redirection; }
        }

        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo
        {
            get { return false; }
        }



        #endregion Properties
    }
}