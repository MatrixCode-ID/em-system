using Em.Api.Core;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Shared;

public partial class EmAppBuilder
{
   private bool _smtpAdded;
   /// <summary>Enables SMTP and named profiles. Requires ta_Meta and ta_Smtp; legacy settings migrate on first use.</summary>
   public void AddSmtp() {
      if (_smtpAdded) throw new InvalidOperationException("SMTP is already registered.");
      _smtpAdded = true;
      AddService<ISmtpService, SmtpService>();
      AddService<ISmtpProfileService, SmtpProfileService>();
      AddClaims(ClaimAction.Create<SmtpService>(ISmtpService.ManagerClaim), ClaimAction.Create<SmtpService>(ISmtpService.SendClaim));
   }
}
