using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>Units of measure, shared by every tenant; all HTTP calls require the Business Module Manager claim.</summary>
public interface IUnitServices : IServices
{
   #region Tables
   /// <summary>Creates a unit. Its code must not exist yet.</summary>
   Task<vi_Unit> PostGetTa_Unit_New(ta_Unit row);
   /// <summary>Saves the symbol and description of a unit, enforcing its update stamp. The code never changes.</summary>
   Task<vi_Unit> PostGetTa_Unit_Update(ta_Unit row);
   #endregion
   #region Views
   /// <summary>Lists every unit, ordered by code.</summary>
   Task<vi_Unit[]> GetVi_Units();
   #endregion
   #region Meta's
   /// <summary>Deletes a unit no product uses, enforcing its update stamp. A unit in use answers a conflict.</summary>
   Task PostMeta_Unit_Delete(string unitId, DateTime ustamp);
   #endregion
}
