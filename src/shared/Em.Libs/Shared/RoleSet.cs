using Em.Api.Core.Models;

namespace Em.Shared
{
   /// <summary>
   /// Changes to the content of one role, sent in one go: claims added and revoked, members added,
   /// removed, and members whose validity changed. Neither a table row nor a list of intentions - it is
   /// the difference between the role's state when opened and the desired state when save is pressed, so
   /// ticking and unticking the same box leaves nothing here.
   /// <para>
   /// Deliberately not a <see cref="DtoPayload{T1,T2,T3,T4,T5}"/>: that payload matches slots by position,
   /// while three of the five lists here have the same type (<see cref="ta_UserRole"/>). A swapped order
   /// would pass the compiler and the runtime, then add members that should have been revoked.
   /// </para>
   /// <para>
   /// The numbers shown by the screen - "2 claims and 1 assignment changed" - are computed from this
   /// object too, through <see cref="ClaimChangeCount"/> and <see cref="AssignmentChangeCount"/>, so no
   /// separate counter can drift from what is actually sent.
   /// </para>
   /// </summary>
   public class RoleSet
   {
      /// <summary>Role whose content changes. Every row below points at this ID.</summary>
      public required string cRoleId { get; init; }

      /// <summary>Claims granted to this role.</summary>
      public ta_RoleClaim[] ClaimsGranted { get; init; } = [];

      /// <summary>Claims revoked from this role.</summary>
      public ta_RoleClaim[] ClaimsRevoked { get; init; } = [];

      /// <summary>New assignments - users who start holding this role.</summary>
      public ta_UserRole[] MembersAdded { get; init; } = [];

      /// <summary>Revoked assignments - users who stop holding this role.</summary>
      public ta_UserRole[] MembersRemoved { get; init; } = [];

      /// <summary>
      /// Assignments that keep running but whose validity changed. Separate from
      /// <see cref="MembersRemoved"/> + <see cref="MembersAdded"/> because revoking and granting again
      /// would erase the record of when the assignment was first created.
      /// </summary>
      public ta_UserRole[] MembersRescheduled { get; init; } = [];

      /// <summary>Number of claim changes: granted plus revoked.</summary>
      public int ClaimChangeCount => ClaimsGranted.Length + ClaimsRevoked.Length;

      /// <summary>Number of assignment changes: added, removed, and those with changed validity.</summary>
      public int AssignmentChangeCount =>
         MembersAdded.Length + MembersRemoved.Length + MembersRescheduled.Length;

      /// <summary>
      /// <c>true</c> when there is no change at all. Callers check it before sending, so a save pressed with
      /// nothing changed does not become a round trip to the server that produces nothing.
      /// </summary>
      public bool IsEmpty => ClaimChangeCount == 0 && AssignmentChangeCount == 0;
   }
}
