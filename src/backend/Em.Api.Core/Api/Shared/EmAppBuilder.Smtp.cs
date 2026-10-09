using Em.Api.Core;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Shared;

public partial class EmAppBuilder
{
   private bool _smtpAdded;
   /// <summary>Enables database-managed SMTP and its manager/send actions. Requires only the core ta_Meta table.</summary>
   public void AddSmtp() {
      if (_smtpAdded) throw new InvalidOperationException("SMTP is already registered.");
      _smtpAdded = true;
      AddService<ISmtpService, SmtpService>();
      AddClaims(ClaimAction.Create<SmtpService>(ISmtpService.ManagerClaim), ClaimAction.Create<SmtpService>(ISmtpService.SendClaim));
   }
}
