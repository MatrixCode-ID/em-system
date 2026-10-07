namespace Em.Shared
{
   /// <summary>
   /// Icons a user can pick to mark a data row - roles, users, modules, categories, and other
   /// entities share the same list so one icon means the same thing wherever it appears. The members
   /// are abstract tokens, not icon names of a particular UI framework: only the meaning of the icon
   /// is stored in data, and which image displays it is up to the presentation layer.
   /// <para>
   /// This list may only grow. Existing members must not be removed or renamed, because the name is
   /// what is stored in data - removing a member would make old rows that use it lose their icon.
   /// </para>
   /// </summary>
   public enum UiIconType
   {
      /// <summary>Not specified - the user has not picked any icon. Deliberately not an image, so a
      /// row without a chosen icon can fall back to its entity's default icon.</summary>
      Unspecified = 0,

      #region Authority

      /// <summary>Oversight, security.</summary>
      Shield,

      /// <summary>Access holder.</summary>
      Key,

      /// <summary>Restriction.</summary>
      Lock,

      /// <summary>Top leadership.</summary>
      Crown,

      /// <summary>Approval, legal.</summary>
      Gavel,

      /// <summary>Approver.</summary>
      CheckCircle,

      #endregion

      #region People

      /// <summary>Individual.</summary>
      User,

      /// <summary>Team, group.</summary>
      Users,

      /// <summary>Manager, executive.</summary>
      UserTie,

      /// <summary>Human resources.</summary>
      IdCard,

      /// <summary>Customer service.</summary>
      Headset,

      /// <summary>Branch, organizational unit.</summary>
      Building,

      #endregion

      #region Operations

      /// <summary>Sales, purchasing.</summary>
      Cart,

      /// <summary>Warehouse, stock.</summary>
      Boxes,

      /// <summary>Shipping, logistics.</summary>
      Truck,

      /// <summary>Production.</summary>
      Factory,

      /// <summary>Engineering, maintenance.</summary>
      Wrench,

      /// <summary>QC, inspection.</summary>
      ClipboardCheck,

      #endregion

      #region Numbers & system

      /// <summary>Analytics, targets.</summary>
      ChartLine,

      /// <summary>Cash, finance.</summary>
      Coins,

      /// <summary>Billing, receivables.</summary>
      Invoice,

      /// <summary>Accounting.</summary>
      Calculator,

      /// <summary>IT, systems.</summary>
      Database,

      /// <summary>Configuration.</summary>
      Gear,

      #endregion
   }
}
