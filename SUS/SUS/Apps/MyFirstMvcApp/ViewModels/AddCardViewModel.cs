﻿namespace BattleCards.ViewModels
{
    public class AddCardViewModel
    {
        public int Attack { get; set; }
        public int Health { get; set; }
        public int Damage =>Health-Attack;
    }
}
