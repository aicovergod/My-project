using System;
using System.Collections.Generic;
using Quests;
using UI.Quests;
using UI.Utilities;
using UnityEngine;
using UnityEngine.UI;
using World;

namespace Quests
{
    /// <summary>
    /// Runtime quest journal window that now renders the QuestList and QuestInfo panels created by <see cref="QuestJournalViewBuilder"/>.
    /// Attach this component to a dedicated persistent GameObject (for example the existing QuestUI prefab) and ensure it is
    /// registered with <see cref="UI.UIManager"/> so the Quest tab button can toggle it. The QuestTabUI button's OnClick should
    /// call <see cref="Open"/> on this component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class QuestUI : ManagedUiWindow
    {
        private static QuestUI instance;

        /// <summary>
        /// Raised whenever the quest UI finishes opening.
        /// </summary>
        public static event Action<QuestUI> QuestUIOpened;

        /// <summary>
        /// Raised whenever the quest UI finishes closing.
        /// </summary>
        public static event Action<QuestUI> QuestUIClosed;

        /// <summary>
        /// Cached singleton-style access for systems that need to query quest visibility.
        /// </summary>
        public static QuestUI Instance => instance;

        /// <summary>
        /// Raised after the quest list data has been rebuilt so gameplay systems can update badges or notifications.
        /// </summary>
        public event Action<IReadOnlyList<QuestListEntryData>> QuestListRefreshed;

        private OverlayCanvasFactory.OverlayCanvasComponents canvasComponents;
        private QuestJournalViewBuilder.QuestJournalViewReferences viewReferences;
        private readonly List<QuestListEntryView> entryViews = new List<QuestListEntryView>();
        private readonly PlayerMovementModalLock movementLock = new PlayerMovementModalLock();
        private QuestJournalPresenter presenter;
        private IQuestJournalDataProvider currentProvider;

        private bool wasOpenBeforeOpen;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            // Ensure the UI survives scene loads and remains in the managed UI registry.
            var persistent = GetComponent<ScenePersistentObject>();
            if (persistent == null)
                persistent = gameObject.AddComponent<ScenePersistentObject>();

            instance = this;
            name = "QuestUI";

            canvasComponents = OverlayCanvasFactory.CreateOverlayCanvas(
                "QuestJournalCanvas",
                new Vector2(1920f, 1080f),
                transform,
                dontDestroyOnLoad: false,
                pixelPerfect: true,
                matchWidthOrHeight: 0f,
                assignToUiLayer: true);

            viewReferences = QuestJournalViewBuilder.Build(canvasComponents.Root.transform);
            viewReferences.CloseButton.onClick.AddListener(Close);
            viewReferences.InfoPanel.BackRequested += HandleBackRequested;
            viewReferences.InfoPanel.SetVisible(false);
            viewReferences.ListPanel.gameObject.SetActive(true);
            viewReferences.ListHeader.text = "Quest List";

            presenter = new QuestJournalPresenter();
            presenter.QuestListChanged += HandleQuestListChanged;
            presenter.SelectedQuestChanged += HandleSelectedQuestChanged;

            SetDataProvider(new PlaceholderQuestDataProvider());

            SetWindowRoot(canvasComponents.Root, deactivateOnAssign: true);
            RegisterWindow();
        }

        private void Start()
        {
            AttemptAttachQuestManager();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                movementLock.Release();
                presenter.QuestListChanged -= HandleQuestListChanged;
                presenter.SelectedQuestChanged -= HandleSelectedQuestChanged;
                presenter.Dispose();
                currentProvider = null;

                if (EvaluateIsOpen())
                    QuestUIClosed?.Invoke(this);

                viewReferences.CloseButton.onClick.RemoveListener(Close);
                viewReferences.InfoPanel.BackRequested -= HandleBackRequested;

                UnregisterWindow();
                instance = null;
            }
        }

        /// <summary>
        /// Allows external systems to force the quest list to refresh (e.g. quest scripts after state changes).
        /// </summary>
        public void RequestRefresh()
        {
            presenter?.Refresh();
        }

        /// <summary>
        /// Switches the UI to the supplied data provider. Use this when a gameplay system wants to present filtered quest sets.
        /// </summary>
        public void OverrideDataProvider(IQuestJournalDataProvider provider)
        {
            SetDataProvider(provider);
        }

        /// <summary>
        /// Resets the UI to the default quest manager provider.
        /// </summary>
        public void RestoreQuestManagerProvider()
        {
            SetDataProvider(QuestManager.Instance != null
                ? new QuestManagerDataProvider(QuestManager.Instance)
                : new PlaceholderQuestDataProvider());
        }

        protected override bool EvaluateIsOpen()
        {
            return canvasComponents.Root.activeSelf;
        }

        protected override void SetWindowActive(bool active)
        {
            canvasComponents.Root.SetActive(active);
        }

        protected override void OnBeforeOpen()
        {
            wasOpenBeforeOpen = EvaluateIsOpen();
            InterfaceTabMutexUtility.CloseAllTabWindowsExcept(this);
            movementLock.Acquire();
            AttemptAttachQuestManager();
            ShowListPanel();
        }

        protected override void OnAfterOpen()
        {
            presenter.Refresh();
            if (!wasOpenBeforeOpen)
                QuestUIOpened?.Invoke(this);
        }

        protected override void OnBeforeClose()
        {
            movementLock.Release();
        }

        protected override void OnAfterClose()
        {
            presenter.ClearSelection();
            viewReferences.InfoPanel.ShowPlaceholder();
            viewReferences.InfoPanel.SetVisible(false);
            viewReferences.ListPanel.gameObject.SetActive(true);
            QuestUIClosed?.Invoke(this);
        }

        private void SetDataProvider(IQuestJournalDataProvider provider)
        {
            currentProvider = provider;
            presenter.SetDataProvider(provider);
        }

        private void AttemptAttachQuestManager()
        {
            if (QuestManager.Instance == null)
                return;

            if (currentProvider is QuestManagerDataProvider)
                return;

            SetDataProvider(new QuestManagerDataProvider(QuestManager.Instance));
        }

        private void HandleQuestListChanged(IReadOnlyList<QuestListEntryData> entries)
        {
            RebuildQuestList(entries);
            QuestListRefreshed?.Invoke(entries);
        }

        private void HandleSelectedQuestChanged(QuestListEntryData quest)
        {
            if (quest == null)
            {
                ShowListPanel();
                viewReferences.InfoPanel.ShowPlaceholder();
                return;
            }

            viewReferences.InfoPanel.ShowQuest(quest);
        }

        private void HandleBackRequested()
        {
            ShowListPanel();
            presenter.ClearSelection();
            viewReferences.InfoPanel.ShowPlaceholder();
        }

        private void HandleQuestEntryClicked(QuestListEntryData quest)
        {
            presenter.SelectQuest(quest);
            viewReferences.ListPanel.gameObject.SetActive(false);
            viewReferences.InfoPanel.SetVisible(true);
        }

        private void ShowListPanel()
        {
            viewReferences.ListPanel.gameObject.SetActive(true);
            viewReferences.InfoPanel.SetVisible(false);
        }

        private void RebuildQuestList(IReadOnlyList<QuestListEntryData> entries)
        {
            foreach (var view in entryViews)
            {
                if (view != null)
                    Destroy(view.gameObject);
            }

            entryViews.Clear();
            if (entries == null)
                return;

            foreach (var entry in entries)
            {
                var view = QuestJournalViewBuilder.CreateListEntry(viewReferences.ListContent);
                view.Initialise(entry, HandleQuestEntryClicked);
                entryViews.Add(view);
            }
        }
    }
}
