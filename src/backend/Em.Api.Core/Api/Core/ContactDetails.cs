using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core;

/// <summary>The write rules of communication and address entries, shared by every contact save.</summary>
internal static class ContactDetails
{
   /// <summary>Issues IDs for entries without one and refuses duplicates. Collection callers supply stable draft IDs.</summary>
   internal static void Prepare(ta_Comm[] comms, ta_Address[] addresses) {
      foreach (var item in comms) if (string.IsNullOrWhiteSpace(item.cCommId)) item.cCommId = Ulid.NewUlid().ToString();
      foreach (var item in addresses) if (string.IsNullOrWhiteSpace(item.cAddressId)) item.cAddressId = Ulid.NewUlid().ToString();
      if (comms.Select(x => x.cCommId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != comms.Length ||
          addresses.Select(x => x.cAddressId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != addresses.Length)
         throw new ActionException("Contact details contain duplicate IDs.", 400);
   }

   /// <summary>
   /// Writes the entries and moves the default state to the given IDs. The caller owns the transaction, has
   /// validated the defaults, and updates the default links on the contact afterwards.
   /// </summary>
   internal static async Task WriteAsync(ApiCoreContext ctx, string contactId, ta_Comm[] comms, ta_Address[] addresses,
      string? commId, string? addressId, bool creating, CancellationToken token) {
      var now = DateTime.UtcNow;
      foreach (var item in comms) {
         var old = await ctx.ta_Comms.AsNoTracking().SingleOrDefaultAsync(x => x.cCommId == item.cCommId, token);
         if ((!string.IsNullOrEmpty(item.cContactId) && !TenantOrg.Same(item.cContactId, contactId)) || old is not null && (creating || !TenantOrg.Same(old.cContactId, contactId)))
            throw new ActionException("The communication must belong to this contact.", 400);
         if (old is not null && old.ustamp != item.ustamp || old is null && item.ustamp != default)
            throw new ActionException("A communication changed. Refresh before saving.", 409);
         var state = TenantOrg.Same(item.cCommId, commId) ? ContactCommunicationState.ActiveAsDefault :
            item.cCommState == ContactCommunicationState.ActiveAsDefault ? ContactCommunicationState.Active : item.cCommState;
         if (old is null) ctx.ta_Comms.Add(new ta_Comm {
            cCommId = item.cCommId, cContactId = contactId, cCommType = item.cCommType, cCommValue = item.cCommValue,
            cCommNote = item.cCommNote, cCommState = state, json_object = item.json_object, ustamp = now, datestamp = now
         });
         else await ctx.ta_Comms.Where(x => x.cCommId == item.cCommId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.cCommType, item.cCommType).SetProperty(x => x.cCommValue, item.cCommValue)
            .SetProperty(x => x.cCommNote, item.cCommNote).SetProperty(x => x.cCommState, state)
            .SetProperty(x => x.ustamp, TenantOrg.Stamp(old.ustamp)), token);
      }
      foreach (var item in addresses) {
         var old = await ctx.ta_Addresses.AsNoTracking().SingleOrDefaultAsync(x => x.cAddressId == item.cAddressId, token);
         if ((!string.IsNullOrEmpty(item.cContactId) && !TenantOrg.Same(item.cContactId, contactId)) || old is not null && (creating || !TenantOrg.Same(old.cContactId, contactId)))
            throw new ActionException("The address must belong to this contact.", 400);
         if (old is not null && old.ustamp != item.ustamp || old is null && item.ustamp != default)
            throw new ActionException("An address changed. Refresh before saving.", 409);
         var state = TenantOrg.Same(item.cAddressId, addressId) ? AddressState.ActiveAsDefault :
            item.cAddressState == AddressState.ActiveAsDefault ? AddressState.Active : item.cAddressState;
         if (old is null) ctx.ta_Addresses.Add(new ta_Address {
            cAddressId = item.cAddressId, cContactId = contactId, cAddressName = item.cAddressName, cAddressLocation = item.cAddressLocation,
            cAddressZip = item.cAddressZip, cAddressState = state, json_object = item.json_object, ustamp = now, datestamp = now
         });
         else await ctx.ta_Addresses.Where(x => x.cAddressId == item.cAddressId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.cAddressName, item.cAddressName).SetProperty(x => x.cAddressLocation, item.cAddressLocation)
            .SetProperty(x => x.cAddressZip, item.cAddressZip).SetProperty(x => x.cAddressState, state)
            .SetProperty(x => x.ustamp, TenantOrg.Stamp(old.ustamp)), token);
      }
      // Demote any previous default omitted by a default-only caller without deleting other details.
      if (commId is not null) await ctx.ta_Comms.Where(x => x.cContactId == contactId && x.cCommId != commId && x.cCommState == ContactCommunicationState.ActiveAsDefault)
         .ExecuteUpdateAsync(s => s.SetProperty(x => x.cCommState, ContactCommunicationState.Active).SetProperty(x => x.ustamp, now), token);
      if (addressId is not null) await ctx.ta_Addresses.Where(x => x.cContactId == contactId && x.cAddressId != addressId && x.cAddressState == AddressState.ActiveAsDefault)
         .ExecuteUpdateAsync(s => s.SetProperty(x => x.cAddressState, AddressState.Active).SetProperty(x => x.ustamp, now), token);
      await ctx.SaveChangesAsync(token);
      // A default chosen from stored entries was not written above, so it takes the default state here.
      if (commId is not null) await ctx.ta_Comms.Where(x => x.cContactId == contactId && x.cCommId == commId && x.cCommState == ContactCommunicationState.Active)
         .ExecuteUpdateAsync(s => s.SetProperty(x => x.cCommState, ContactCommunicationState.ActiveAsDefault).SetProperty(x => x.ustamp, now), token);
      if (addressId is not null) await ctx.ta_Addresses.Where(x => x.cContactId == contactId && x.cAddressId == addressId && x.cAddressState == AddressState.Active)
         .ExecuteUpdateAsync(s => s.SetProperty(x => x.cAddressState, AddressState.ActiveAsDefault).SetProperty(x => x.ustamp, now), token);
   }
}
