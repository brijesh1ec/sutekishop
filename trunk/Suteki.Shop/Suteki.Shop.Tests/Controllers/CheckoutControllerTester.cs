using System;
using NUnit.Framework;
using Rhino.Mocks;
using Suteki.Common.Repositories;
using Suteki.Common.TestHelpers;
using Suteki.Shop.Controllers;
using Suteki.Shop.Services;
using Suteki.Shop.ViewData;

// ReSharper disable InconsistentNaming
namespace Suteki.Shop.Tests.Controllers
{
	[TestFixture]
	public class CheckoutControllerTester
	{
		CheckoutController controller;
		IRepository<Basket> basketRepository;
		IUserService userService;
	    IBasketService basketService;
		IRepository<CardType> cardTypeRepository;
		FakeRepository<Order> orderRepository;
		IUnitOfWorkManager unitOfWorkManager;
		IEmailService emailService;
		IRepository<MailingListSubscription> subscriptionRepository;
	    ICheckoutService checkoutService;

		[SetUp]
		public void Setup()
		{
			basketRepository = MockRepository.GenerateStub<IRepository<Basket>>();
			unitOfWorkManager = MockRepository.GenerateStub<IUnitOfWorkManager>();

			userService = MockRepository.GenerateStub<IUserService>();
		    basketService = MockRepository.GenerateStub<IBasketService>();
			cardTypeRepository = MockRepository.GenerateStub<IRepository<CardType>>();
			orderRepository = new FakeRepository<Order>();
			subscriptionRepository = MockRepository.GenerateStub<IRepository<MailingListSubscription>>();
			emailService = MockRepository.GenerateStub<IEmailService>();
		    MockRepository.GenerateStub<IEncryptionService>();
		    checkoutService = MockRepository.GenerateStub<ICheckoutService>();

			var mocks = new MockRepository(); //TODO: No need to partial mock once email sending is fixed
			controller = new CheckoutController(
				basketRepository,
				userService,
				cardTypeRepository,
				orderRepository,
				unitOfWorkManager,
				emailService,
				subscriptionRepository,
                basketService,
                checkoutService
			);
			mocks.ReplayAll();
			userService.Expect(us => us.CurrentUser).Return(new User { Id = 4, Role = Role.Administrator });
		}

		[Test]
		public void Index_ShouldDisplayCheckoutForm() {
			const int basketId = 6;
		    var basket = new Basket
		    {
                Country = new Country(),
                Id = basketId
		    };
		    basketRepository.Stub(b => b.GetById(basketId)).Return(basket);

		    var visa = new CardType();
			cardTypeRepository.Stub(ctr => ctr.GetById(CardType.VisaDeltaElectronId))
                .Return(visa);

			// exercise Checkout action
		    controller.Index(basketId)
		        .ReturnsViewResult()
		        .WithModel<CheckoutViewData>()
                .AssertAreSame(basket.Country, vd => vd.CardContactCountry)
                .AssertAreSame(basket.Country, vd => vd.DeliveryContactCountry)
		        .AssertAreSame(visa, vd => vd.CardCardType)
		        .AssertAreEqual(basketId, vd => vd.BasketId)
                .AssertIsTrue(vd => vd.UseCardholderContact);
		}

		[Test]
		public void Index_should_load_order_from_tempdata()
		{
		    var checkoutViewData = new CheckoutViewData();
            controller.TempData["CheckoutViewData"] = checkoutViewData;

			controller.Index(4)
				.ReturnsViewResult()
                .WithModel<CheckoutViewData>()
                .AssertAreSame(checkoutViewData, vd => vd);
		}

		[Test]
		public void IndexWithPost_ShouldCreateANewOrder()
		{
		    var checkoutViewData = new CheckoutViewData();

		    var createdOrder = new Order();
		    Order savedOrder = null;
		    orderRepository.SaveOrUpdateDelegate = entity =>
		    {
		        savedOrder = entity;
		        savedOrder.Id = 4;
		    };

		    checkoutService.Stub(c => c.OrderFromCheckoutViewData(checkoutViewData, controller.ModelState))
                .Return(createdOrder);

		    controller.Index(checkoutViewData)
				.ReturnsRedirectToRouteResult()
				.ToController("Checkout")
				.ToAction("Confirm")
				.WithRouteValue("id", "4");

            savedOrder.ShouldBeTheSameAs(createdOrder);
            controller.ModelState.IsValid.ShouldBeTrue();
		}

	    [Test]
		public void IndexWithPost_ShouldRenderViewOnError()
		{
			controller.ModelState.AddModelError("foo", "bar");
            controller.Index(new CheckoutViewData())
				.ReturnsViewResult()
                .WithModel<CheckoutViewData>();
		}

		[Test]
		public void Confirm_DisplaysConfirm()
		{
			var order = new Order();
		    orderRepository.EntityFactory = id => order;
			controller.Confirm(5)
				.ReturnsViewResult()
				.WithModel<ShopViewData>()
				.AssertAreSame(order, x => x.Order);
		}

		[Test]
		public void ConfirmWithPost_UpdatesOrderStatus()
		{
			var order = new Order { Id = 5 };

			controller.Confirm(order)
				.ReturnsRedirectToRouteResult()
				.ToController("Order")
				.ToAction("Item")
				.WithRouteValue("id", "5");

			emailService.AssertWasCalled(x => x.SendOrderConfirmation(order));
		}

		[Test]
		public void ConfirmWithPost_CreatesMailingListSubscriptionForDeliveryContact()
		{
			var order = new Order { DeliveryContact = new Contact(), ContactMe = true, Email = "foo@bar.com"};
			MailingListSubscription subscription = null;

			subscriptionRepository.Expect(x => x.SaveOrUpdate(Arg<MailingListSubscription>.Is.Anything))
				.Do(new Action<MailingListSubscription>(x => subscription = x));

			controller.Confirm(order);

			subscription.ShouldNotBeNull();
			subscription.Contact.ShouldBeTheSameAs(order.DeliveryContact);
			subscription.Email.ShouldEqual("foo@bar.com");
		}

		[Test]
		public void ConfirmWithPost_CreatesMailingListSubscriptionForCardContact()
		{
			var order = new Order
			{
			    CardContact = new Contact(), 
                ContactMe = true, 
                Email = "foo@bar.com", 
                UseCardHolderContact = true
			};

			MailingListSubscription subscription = null;

			subscriptionRepository.Expect(x => x.SaveOrUpdate(Arg<MailingListSubscription>.Is.Anything))
				.Do(new Action<MailingListSubscription>(x => subscription = x));

			controller.Confirm(order);

			subscription.ShouldNotBeNull();
			subscription.Contact.ShouldBeTheSameAs(order.CardContact);
			subscription.Email.ShouldEqual("foo@bar.com");
		}

	    [Test]
	    public void UpdateCountry_should_update_the_country()
	    {
	        const int basketId = 94;
	        var basket = new Basket();
	        basketRepository.Stub(b => b.GetById(basketId)).Return(basket);

	        var checkoutViewData = new CheckoutViewData
	        {
	            BasketId = basketId,
	            UseCardholderContact = true,
	            CardContactCountry = new Country(),
                DeliveryContactCountry = new Country()
	        };

	        controller.UpdateCountry(checkoutViewData)
	            .ReturnsRedirectToRouteResult()
	            .WithRouteValue("action", "Index");

            basket.Country.ShouldBeTheSameAs(checkoutViewData.CardContactCountry);

            controller.TempData["CheckoutViewData"].ShouldBeTheSameAs(checkoutViewData);
	    }
	}
}
// ReSharper restore InconsistentNaming
