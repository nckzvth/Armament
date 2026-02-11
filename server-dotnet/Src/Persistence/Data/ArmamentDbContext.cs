using Microsoft.EntityFrameworkCore;

namespace Armament.Persistence.Data;

public sealed class ArmamentDbContext(DbContextOptions<ArmamentDbContext> options) : DbContext(options)
{
    public DbSet<AccountEntity> Accounts => Set<AccountEntity>();
    public DbSet<AccountCharacterEntity> AccountCharacters => Set<AccountCharacterEntity>();
    public DbSet<CharacterEntity> Characters => Set<CharacterEntity>();
    public DbSet<InventoryItemEntity> InventoryItems => Set<InventoryItemEntity>();
    public DbSet<QuestProgressEntity> QuestProgress => Set<QuestProgressEntity>();
    public DbSet<LearnedRecipeEntity> LearnedRecipes => Set<LearnedRecipeEntity>();
    public DbSet<LootClaimEntity> LootClaims => Set<LootClaimEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountEntity>(entity =>
        {
            entity.ToTable("accounts");
            entity.HasKey(x => x.AccountId);
            entity.Property(x => x.AccountId).ValueGeneratedNever();
            entity.Property(x => x.ExternalSubject).HasMaxLength(128);
            entity.Property(x => x.DisplayName).HasMaxLength(64);
            entity.Property(x => x.CreatedUtc).HasColumnType("timestamp with time zone");
            entity.Property(x => x.UpdatedUtc).HasColumnType("timestamp with time zone");
            entity.HasIndex(x => x.ExternalSubject).IsUnique().HasDatabaseName("ux_accounts_external_subject");
        });

        modelBuilder.Entity<AccountCharacterEntity>(entity =>
        {
            entity.ToTable("account_characters");
            entity.HasKey(x => x.AccountCharacterId);
            entity.Property(x => x.CharacterName).HasMaxLength(64);
            entity.Property(x => x.CreatedUtc).HasColumnType("timestamp with time zone");
            entity.HasIndex(x => new { x.AccountId, x.SlotIndex }).IsUnique().HasDatabaseName("ux_account_character_slot");
            entity.HasIndex(x => new { x.AccountId, x.CharacterId }).IsUnique().HasDatabaseName("ux_account_character_mapping");
            entity.HasOne(x => x.Account)
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Character)
                .WithMany(x => x.AccountCharacters)
                .HasForeignKey(x => x.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CharacterEntity>(entity =>
        {
            entity.ToTable("characters");
            entity.HasKey(x => x.CharacterId);
            entity.Property(x => x.CharacterId).ValueGeneratedNever();
            entity.Property(x => x.Name).HasMaxLength(64);
            entity.Property(x => x.BaseClassId).HasMaxLength(32);
            entity.Property(x => x.SpecId).HasMaxLength(64);
            entity.Property(x => x.GearJson).HasColumnType("jsonb");
            entity.Property(x => x.InventoryJson).HasColumnType("jsonb");
            entity.Property(x => x.LearnedRecipesJson).HasColumnType("jsonb");
            entity.Property(x => x.QuestProgressJson).HasColumnType("jsonb");
            entity.Property(x => x.CreatedUtc).HasColumnType("timestamp with time zone");
            entity.Property(x => x.UpdatedUtc).HasColumnType("timestamp with time zone");
            entity.HasIndex(x => x.Name).HasDatabaseName("ix_characters_name");
        });

        modelBuilder.Entity<InventoryItemEntity>(entity =>
        {
            entity.ToTable("inventory_items");
            entity.HasKey(x => x.InventoryItemId);
            entity.Property(x => x.ItemCode).HasMaxLength(64);
            entity.HasIndex(x => new { x.CharacterId, x.ItemCode }).IsUnique().HasDatabaseName("ux_inventory_character_item");
            entity.HasOne(x => x.Character)
                .WithMany(x => x.InventoryItems)
                .HasForeignKey(x => x.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QuestProgressEntity>(entity =>
        {
            entity.ToTable("quest_progress");
            entity.HasKey(x => x.QuestProgressId);
            entity.Property(x => x.QuestCode).HasMaxLength(64);
            entity.HasIndex(x => new { x.CharacterId, x.QuestCode }).IsUnique().HasDatabaseName("ux_quest_character_code");
            entity.HasOne(x => x.Character)
                .WithMany()
                .HasForeignKey(x => x.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LearnedRecipeEntity>(entity =>
        {
            entity.ToTable("learned_recipes");
            entity.HasKey(x => x.LearnedRecipeId);
            entity.Property(x => x.RecipeCode).HasMaxLength(64);
            entity.HasIndex(x => new { x.CharacterId, x.RecipeCode }).IsUnique().HasDatabaseName("ux_recipe_character_code");
            entity.HasOne(x => x.Character)
                .WithMany()
                .HasForeignKey(x => x.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LootClaimEntity>(entity =>
        {
            entity.ToTable("loot_claims");
            entity.HasKey(x => x.LootClaimId);
            entity.Property(x => x.PickupToken).HasMaxLength(128);
            entity.Property(x => x.ClaimedAtUtc).HasColumnType("timestamp with time zone");
            entity.HasIndex(x => x.PickupToken).IsUnique().HasDatabaseName("ux_loot_claims_pickup_token");
            entity.HasOne(x => x.Character)
                .WithMany()
                .HasForeignKey(x => x.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
