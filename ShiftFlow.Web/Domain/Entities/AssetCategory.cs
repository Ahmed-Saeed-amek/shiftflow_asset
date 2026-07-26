namespace ShiftFlow.Domain.Entities;
public class AssetCategory
{
    public int Id{get;set;} public string Name{get;set;}=string.Empty; public string? NameAr{get;set;}
    /// <summary>Null for a top-level category. Exactly 2 levels are enforced — a category with a ParentCategoryId can't itself have children (see AssetCategoriesController).</summary>
    public int? ParentCategoryId{get;set;} public virtual AssetCategory? ParentCategory{get;set;}
    public DateTime CreatedDate{get;set;}=DateTime.UtcNow;
    public virtual ICollection<AssetCategory> Subcategories{get;set;}=new List<AssetCategory>();
    public virtual ICollection<Asset> Assets{get;set;}=new List<Asset>();
    public virtual ICollection<AssetActionType> ActionTypes{get;set;}=new List<AssetActionType>();
}
