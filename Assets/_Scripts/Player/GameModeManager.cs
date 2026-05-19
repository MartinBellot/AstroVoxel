// ============================================================
//  GameModeManager.cs
//  Gère le mode de jeu (Créatif / Survie).
//  Classe statique — accessible partout sans MonoBehaviour.
// ============================================================

using System;
using UnityEngine.SceneManagement;

namespace AstroVoxel.Player
{
    public enum GameMode { Creative, Survival }

    public static class GameModeManager
    {
        // ── État ──────────────────────────────────────────────
        public static GameMode Current { get; private set; } = GameMode.Creative;

        // ── Événement diffusé à tous les abonnés ──────────────
        public static event Action<GameMode> OnGameModeChanged;

        // ── API publique ──────────────────────────────────────

        public static void SetMode(GameMode mode)
        {
            if (Current == mode) return;
            Current = mode;
            OnGameModeChanged?.Invoke(mode);
        }

        public static bool IsSurvival  => Current == GameMode.Survival;
        public static bool IsCreative  => Current == GameMode.Creative;

        /// <summary>
        /// Appelé lors d'un rechargement de scène (/restart) pour revenir en créatif.
        /// </summary>
        public static void ResetToCreative()
        {
            Current = GameMode.Creative;
            // Pas d'event : la scène se recharge, tous les abonnés sont détruits.
        }
    }
}
