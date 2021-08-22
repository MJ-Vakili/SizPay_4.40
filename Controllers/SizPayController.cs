//http://nopfarsi .ir
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using NopFarsi.Payment.SizPay.Models;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Security;
using Nop.Services.Stores;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;
using Nop.Services.Messages;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;

namespace NopFarsi.Payment.SizPay.Controllers
{
    public class SizPayController : BasePaymentController
    {
        #region Fields

        private readonly IWorkContext _workContext;
        private readonly IStoreService _storeService;
        private readonly ISettingService _settingService;
        private readonly IPaymentService _paymentService;
        private readonly IOrderService _orderService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IPermissionService _permissionService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly ILocalizationService _localizationService;
        private readonly IStoreContext _storeContext;
        private readonly ILogger _logger;
        private readonly IWebHelper _webHelper;
        private readonly PaymentSettings _paymentSettings;
        private readonly PayPaymentSettings _payPaymentSettings;
        private readonly INotificationService notificationService;
        private readonly ShoppingCartSettings _shoppingCartSettings;

        #endregion Fields

        #region Ctor

        public SizPayController(IWorkContext workContext,
            IStoreService storeService,
            ISettingService settingService,
            IPaymentService paymentService,
            IOrderService orderService,
            IOrderProcessingService orderProcessingService,
            IPermissionService permissionService,
            IGenericAttributeService genericAttributeService,
            ILocalizationService localizationService,
            IStoreContext storeContext,
            ILogger logger,
            IWebHelper webHelper,
            PaymentSettings paymentSettings,
            PayPaymentSettings payPaymentSettings,
            INotificationService notificationService,

            ShoppingCartSettings shoppingCartSettings)
        {
            _workContext = workContext;
            _storeService = storeService;
            _settingService = settingService;
            _paymentService = paymentService;
            _orderService = orderService;
            _orderProcessingService = orderProcessingService;
            _permissionService = permissionService;
            _genericAttributeService = genericAttributeService;
            _localizationService = localizationService;
            _storeContext = storeContext;
            _logger = logger;
            _webHelper = webHelper;
            _paymentSettings = paymentSettings;
            _payPaymentSettings = payPaymentSettings;
            this.notificationService = notificationService;
            _shoppingCartSettings = shoppingCartSettings;
        }

        #endregion Ctor

        #region Methods

        public IActionResult Pay(string MerchantID, string TerminalID, string Token)
        {
            ViewBag.MerchantID = MerchantID;
            ViewBag.TerminalID = TerminalID;
            ViewBag.Token = Token;
            return base.View("~/Plugins/NopFarsi.Payment.SizPay/Views/Pay.cshtml");
        }

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //load settings for a chosen store scope
            var storeScope = await this._storeContext.GetActiveStoreScopeConfigurationAsync();
            var payPaymentSettings = await _settingService.LoadSettingAsync<PayPaymentSettings>(storeScope);

            var model = new ConfigurationModel
            {
                MerchantID = payPaymentSettings.MerchentId,
                UserName = payPaymentSettings.UserName,
                Password = payPaymentSettings.Password,
                TerminalID = payPaymentSettings.TerminalId,
                IsToman = payPaymentSettings.IsToman
            };
            if (storeScope > 0)
            {
                model.MerchantID_OverrideForStore = await _settingService.SettingExistsAsync(payPaymentSettings, x => x.MerchentId, storeScope);
                model.Password_OverrideForStore = await _settingService.SettingExistsAsync(payPaymentSettings, x => x.Password, storeScope);
                model.TerminalID_OverrideForStore = await _settingService.SettingExistsAsync(payPaymentSettings, x => x.TerminalId, storeScope);
                model.IsToman_OverrideForStore = await _settingService.SettingExistsAsync(payPaymentSettings, x => x.IsToman, storeScope);
            }

            return View("~/Plugins/NopFarsi.Payment.SizPay/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AuthorizeAdmin]
        //[AdminAntiForgery]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return await Configure();

            //load settings for a chosen store scope
            var storeScope = await this._storeContext.GetActiveStoreScopeConfigurationAsync();
            var payPaymentSettings = await _settingService.LoadSettingAsync<PayPaymentSettings>(storeScope);

            //save settings
            payPaymentSettings.IsToman = model.IsToman;
            payPaymentSettings.MerchentId = model.MerchantID;
            payPaymentSettings.UserName = model.UserName;
            payPaymentSettings.TerminalId = model.TerminalID;
            payPaymentSettings.Password = model.Password;
            payPaymentSettings.IsToman = model.IsToman;

            /* We do not clear cache after each setting update.
             * This behavior can increase performance because cached settings will not be cleared
             * and loaded from database after each update */
            await _settingService.SaveSettingOverridablePerStoreAsync(payPaymentSettings, x => x.IsToman, model.IsToman_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(payPaymentSettings, x => x.MerchentId, model.MerchantID_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(payPaymentSettings, x => x.UserName, model.UserName_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(payPaymentSettings, x => x.TerminalId, model.TerminalID_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(payPaymentSettings, x => x.IsToman, model.IsToman_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(payPaymentSettings, x => x.Password, model.Password_OverrideForStore, storeScope, false);

            //now clear settings cache
            await _settingService.ClearCacheAsync();

            this.notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

            return await Configure();
        }

        //action displaying notification (warning) to a store owner about inaccurate Pay rounding
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> RoundingWarning(bool passProductNamesAndTotals)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //prices and total aren't rounded, so display warning
            if (passProductNamesAndTotals && !_shoppingCartSettings.RoundPricesDuringCalculation)
                return Json(new { Result = await _localizationService.GetResourceAsync("Plugins.Payments.Pay.RoundingWarning") });

            return Json(new { Result = string.Empty });
        }

        //[HttpPost]
        public async Task<IActionResult> Verify(int ResCod, string Message, int MrchID, string TrmnlID, int InvoiceNo, string ExtraInf, string Token)
        {
            if (ResCod == 0)
            {
                var order = await _orderService.GetOrderByIdAsync(InvoiceNo);
                if (order.AuthorizationTransactionCode == Token)
                {
                    ServicePointManager.ServerCertificateValidationCallback =
                               delegate (object s, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) { return true; };

                    var kimial = new KimialPG.KimiaIPGRouteServiceSoapClient(KimialPG.KimiaIPGRouteServiceSoapClient.EndpointConfiguration.KimiaIPGRouteServiceSoap);
                    var result = kimial.ConfirmAsync(new KimialPG.PaymentRequest()
                    {
                        MerchantID = _payPaymentSettings.MerchentId,
                        Password = _payPaymentSettings.Password,
                        UserName = _payPaymentSettings.UserName,
                        TerminalID = _payPaymentSettings.TerminalId,
                        Token = Token
                    }).Result.Body.ConfirmResult;
                    if (result.ResCod == 0)
                    {
                        await this._orderService.InsertOrderNoteAsync(new OrderNote
                        {
                            OrderId = order.Id,
                            Note = result.TraceNo + " " + result.TransNo,
                            DisplayToCustomer = false,
                            CreatedOnUtc = DateTime.UtcNow,
                        });
                        await _orderProcessingService.MarkOrderAsPaidAsync(order);
                        return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
                    }
                    else
                    {
                        await this._orderService.InsertOrderNoteAsync(new OrderNote
                        {
                            OrderId = order.Id,
                            Note = result.Message,
                            DisplayToCustomer = false,
                            CreatedOnUtc = DateTime.UtcNow,
                        });
                    }
                    await _orderService.UpdateOrderAsync(order);
                }
            }
            return base.RedirectToAction("Error", "SizPay", new
            {
                ErrorCode = ResCod,
                Message = Message
            });
        }

        public IActionResult Error(string ErrorCode, string Message)
        {
            ViewBag.ErrorCode = ErrorCode;
            ViewBag.Message = Message;
            return View("~/Plugins/NopFarsi.Payment.SizPay/Views/Error.cshtml");
        }


        #endregion Methods
    }
}