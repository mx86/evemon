﻿using System;
using System.Collections.Generic;
using System.Linq;
using EVEMon.Common.Attributes;
using EVEMon.Common.Collections;
using EVEMon.Common.Serialization.API;
using EVEMon.Common.Service;

namespace EVEMon.Common.Models
{
    /// <summary>
    /// Represents a character's skills queue.
    /// </summary>
    [EnforceUIThreadAffinity]
    public sealed class SkillQueue : ReadonlyCollection<QueuedSkill>
    {
        private readonly CCPCharacter m_character;
        private readonly DateTime m_startTime = DateTime.UtcNow;


        #region Constructor

        /// <summary>
        /// Default constructor, only used by <see cref="Character"/>
        /// </summary>
        /// <param name="character">The character this collection is bound to.</param>
        internal SkillQueue(CCPCharacter character)
        {
            m_character = character;

            EveMonClient.TimerTick += EveMonClient_TimerTick;
        }

        #endregion

        /// <summary>
        /// Called when the object gets disposed.
        /// </summary>
        internal void Dispose()
        {
            EveMonClient.TimerTick -= EveMonClient_TimerTick;
        }

        #region Properties

        /// <summary>
        /// Gets true when the character is currently training (non-empty and non-paused skill queue), false otherwise.
        /// </summary>
        public bool IsTraining
        {
            get
            {
                return !IsPaused && Items.Any();
            }
        }

        /// <summary>
        /// Gets the last completed skill.
        /// </summary>
        public QueuedSkill LastCompleted { get; private set; }

        /// <summary>
        /// Gets the training end time (UTC).
        /// </summary>
        public DateTime EndTime
        {
            get { return !Items.Any() ? DateTime.UtcNow : Items.Last().EndTime; }
        }

        /// <summary>
        /// Gets the skill currently in training.
        /// </summary>
        public QueuedSkill CurrentlyTraining
        {
            get { return Items.FirstOrDefault(); }
        }

        /// <summary>
        /// Gets true whether the skill queue is currently paused.
        /// </summary>
        public bool IsPaused { get; private set; }

        #endregion


        #region Update

        /// <summary>
        /// When the timer ticks, on every second, we update the skill.
        /// </summary>
        private void UpdateOnTimerTick()
        {
            List<QueuedSkill> skillsCompleted = new List<QueuedSkill>();

            // Pops all the completed skills
            while (Items.Any())
            {
                QueuedSkill skill = Items.First();

                // If the skill is not completed, we jump out of the loop
                if (skill.EndTime > DateTime.UtcNow)
                    break;

                // The skill has been completed
                if (skill.Skill != null)
                    skill.Skill.MarkAsCompleted();

                skillsCompleted.Add(skill);
                LastCompleted = skill;
                Items.Remove(skill);

                // Sends an email alert
                if (!Settings.IsRestoringSettings && Settings.Notifications.SendMailAlert)
                    Emailer.SendSkillCompletionMail(Items, skill, m_character);

                // Sends a notification
                EveMonClient.Notifications.NotifySkillCompletion(m_character, skillsCompleted);
            }

            // At least one skill completed ?
            if (skillsCompleted.Any())
                EveMonClient.OnCharacterQueuedSkillsCompleted(m_character, skillsCompleted);
        }

        #endregion


        #region Global Event Handlers

        /// <summary>
        /// Handles the TimerTick event of the EveMonClient control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void EveMonClient_TimerTick(object sender, EventArgs e)
        {
            if (IsPaused || !m_character.Monitored)
                return;

            UpdateOnTimerTick();
        }

        #endregion


        #region Importation/Exportation

        /// <summary>
        /// Generates a deserialization object.
        /// </summary>
        /// <returns></returns>
        internal IEnumerable<SerializableQueuedSkill> Export()
        {
            return Items.Select(skill => skill.Export());
        }

        /// <summary>
        /// Imports data from a serialization object.
        /// </summary>
        /// <param name="serial"></param>
        internal void Import(IEnumerable<SerializableQueuedSkill> serial)
        {
            IsPaused = false;

            // If the queue is paused, CCP sends empty start and end time
            // So we base the start time on when the skill queue was started
            DateTime startTimeWhenPaused = m_startTime;

            // Imports the queued skills and checks whether they are paused
            Items.Clear();
            foreach (SerializableQueuedSkill serialSkill in serial)
            {
                // When the skill queue is paused, startTime and endTime are empty in the XML document
                // As a result, the serialization leaves the DateTime with its default value
                if (serialSkill.EndTime == DateTime.MinValue)
                    IsPaused = true;

                // Creates the skill queue
                Items.Add(new QueuedSkill(m_character, serialSkill, IsPaused, ref startTimeWhenPaused));
            }

            // Fires the event regarding the character skill queue update
            EveMonClient.OnCharacterSkillQueueUpdated(m_character);
        }

        #endregion
    }
}