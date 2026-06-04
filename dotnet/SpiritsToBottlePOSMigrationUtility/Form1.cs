using System.Diagnostics;
using System.Text;
using SpiritsToBottlePOSMigrationUtility.Models;
using SpiritsToBottlePOSMigrationUtility.Services;

namespace SpiritsToBottlePOSMigrationUtility;

public partial class Form1 : Form
{
    private readonly IMigrationService _migrationService;
    private readonly UserPreferencesService _preferencesService = new();
    private readonly FlowLayoutPanel modeBar = new();
    private readonly RadioButton standardModeRadioButton = new();
    private readonly RadioButton guidedModeRadioButton = new();
    private readonly Panel contentPanel = new();
    private readonly Button openZipFolderButton = new();
    private readonly TableLayoutPanel guidedLayout = new();
    private readonly Label guidedStepLabel = new();
    private readonly Label guidedInstructionLabel = new();
    private readonly Panel guidedContentPanel = new();
    private readonly FlowLayoutPanel guidedButtonBar = new();
    private readonly Button guidedBackButton = new();
    private readonly Button guidedNextButton = new();
    private readonly Button guidedRunButton = new();
    private readonly TableLayoutPanel guidedSourceLayout = new();
    private readonly TextBox guidedSourceDirectoryTextBox = new();
    private readonly Button guidedBrowseSourceButton = new();
    private readonly TableLayoutPanel guidedOutputLayout = new();
    private readonly TextBox guidedOutputDirectoryTextBox = new();
    private readonly Button guidedBrowseOutputButton = new();
    private readonly TableLayoutPanel guidedExportsLayout = new();
    private readonly CheckBox guidedDepartmentsCheckBox = new();
    private readonly CheckBox guidedVendorsCheckBox = new();
    private readonly CheckBox guidedCustomersCheckBox = new();
    private readonly CheckBox guidedInventoryCheckBox = new();
    private readonly CheckBox guidedGiftCardsCheckBox = new();
    private readonly TableLayoutPanel guidedInventoryOptionsLayout = new();
    private readonly CheckBox guidedIncludeInactiveCheckBox = new();
    private readonly CheckBox guidedAddQtyOneIfMissingCheckBox = new();
    private readonly CheckBox guidedDefaultPriceLevelCheckBox = new();
    private readonly ComboBox guidedDefaultPriceLevelComboBox = new();
    private readonly TableLayoutPanel guidedRunLayout = new();
    private readonly Label guidedRunSummaryLabel = new();
    private bool _isBusy;
    private bool _isChangingMode;
    private UiMode _uiMode = UiMode.Standard;
    private GuidedStep _guidedStep = GuidedStep.Source;
    private string _lastZipFilePath = string.Empty;

    public Form1()
        : this(new MigrationService())
    {
    }

    public Form1(IMigrationService migrationService)
    {
        _migrationService = migrationService;
        InitializeComponent();
        WireEvents();
        ApplyDefaults();
    }

    private void WireEvents()
    {
        browseSourceButton.Click += (_, _) => BrowseForFolder(sourceDirectoryTextBox, "Choose the Spirits/KSV data directory");
        browseOutputButton.Click += (_, _) => BrowseForOutputFolder(outputDirectoryTextBox);
        guidedBrowseSourceButton.Click += (_, _) => BrowseForFolder(guidedSourceDirectoryTextBox, "Choose the Spirits/KSV data directory");
        guidedBrowseOutputButton.Click += (_, _) => BrowseForOutputFolder(guidedOutputDirectoryTextBox);
        closeButton.Click += (_, _) => Close();
        openZipFolderButton.Click += (_, _) => OpenZipFolder();
        startButton.Click += async (_, _) => await StartMigrationAsync();
        guidedRunButton.Click += async (_, _) => await StartMigrationAsync();
        guidedBackButton.Click += (_, _) => MoveGuidedBack();
        guidedNextButton.Click += (_, _) => MoveGuidedNext();
        standardModeRadioButton.CheckedChanged += (_, _) =>
        {
            if (standardModeRadioButton.Checked)
            {
                SetUiMode(UiMode.Standard);
            }
        };
        guidedModeRadioButton.CheckedChanged += (_, _) =>
        {
            if (guidedModeRadioButton.Checked)
            {
                SetUiMode(UiMode.Guided);
            }
        };

        sourceDirectoryTextBox.TextChanged += (_, _) => RefreshExportAvailability();
        guidedSourceDirectoryTextBox.TextChanged += (_, _) => RefreshExportAvailability();
        outputDirectoryTextBox.TextChanged += (_, _) => SyncOutputDirectory(outputDirectoryTextBox, guidedOutputDirectoryTextBox);
        guidedOutputDirectoryTextBox.TextChanged += (_, _) => SyncOutputDirectory(guidedOutputDirectoryTextBox, outputDirectoryTextBox);

        WireStandardOptionEvents();
        WireGuidedOptionEvents();
    }

    private void WireStandardOptionEvents()
    {
        departmentsCheckBox.CheckedChanged += (_, _) => RefreshPlannedOutputs();
        vendorsCheckBox.CheckedChanged += (_, _) => RefreshPlannedOutputs();
        customersCheckBox.CheckedChanged += (_, _) => RefreshPlannedOutputs();
        inventoryCheckBox.CheckedChanged += (_, _) =>
        {
            RefreshPlannedOutputs();
            RefreshExportAvailability();
        };
        giftCardsCheckBox.CheckedChanged += (_, _) => RefreshPlannedOutputs();
        includeInactiveCheckBox.CheckedChanged += (_, _) => RefreshPlannedOutputs();
        addQtyOneIfMissingCheckBox.CheckedChanged += (_, _) => RefreshPlannedOutputs();
        defaultPriceLevelCheckBox.CheckedChanged += (_, _) =>
        {
            RefreshExportAvailability();
            RefreshPlannedOutputs();
        };
        defaultPriceLevelComboBox.SelectedIndexChanged += (_, _) => RefreshPlannedOutputs();
    }

    private void WireGuidedOptionEvents()
    {
        guidedDepartmentsCheckBox.CheckedChanged += (_, _) => RefreshPlannedOutputs();
        guidedVendorsCheckBox.CheckedChanged += (_, _) => RefreshPlannedOutputs();
        guidedCustomersCheckBox.CheckedChanged += (_, _) => RefreshPlannedOutputs();
        guidedInventoryCheckBox.CheckedChanged += (_, _) =>
        {
            RefreshPlannedOutputs();
            RefreshExportAvailability();
            UpdateGuidedStep();
        };
        guidedGiftCardsCheckBox.CheckedChanged += (_, _) => RefreshPlannedOutputs();
        guidedIncludeInactiveCheckBox.CheckedChanged += (_, _) => RefreshPlannedOutputs();
        guidedAddQtyOneIfMissingCheckBox.CheckedChanged += (_, _) => RefreshPlannedOutputs();
        guidedDefaultPriceLevelCheckBox.CheckedChanged += (_, _) =>
        {
            RefreshExportAvailability();
            RefreshPlannedOutputs();
        };
        guidedDefaultPriceLevelComboBox.SelectedIndexChanged += (_, _) => RefreshPlannedOutputs();
    }

    private void ApplyDefaults()
    {
        departmentsCheckBox.Checked = true;
        vendorsCheckBox.Checked = true;
        customersCheckBox.Checked = true;
        inventoryCheckBox.Checked = true;
        giftCardsCheckBox.Checked = true;
        includeInactiveCheckBox.Checked = false;
        addQtyOneIfMissingCheckBox.Checked = true;
        defaultPriceLevelCheckBox.Checked = true;
        defaultPriceLevelComboBox.SelectedIndex = 0;

        guidedDepartmentsCheckBox.Checked = true;
        guidedVendorsCheckBox.Checked = true;
        guidedCustomersCheckBox.Checked = true;
        guidedInventoryCheckBox.Checked = true;
        guidedGiftCardsCheckBox.Checked = true;
        guidedIncludeInactiveCheckBox.Checked = false;
        guidedAddQtyOneIfMissingCheckBox.Checked = true;
        guidedDefaultPriceLevelCheckBox.Checked = true;
        guidedDefaultPriceLevelComboBox.SelectedIndex = 0;

        var lastOutputDirectory = _preferencesService.LoadLastOutputDirectory();
        if (!string.IsNullOrWhiteSpace(lastOutputDirectory))
        {
            outputDirectoryTextBox.Text = lastOutputDirectory;
            guidedOutputDirectoryTextBox.Text = lastOutputDirectory;
        }

        statusValueLabel.Text = "Waiting to generate";
        statusValueLabel.ForeColor = Color.FromArgb(72, 92, 112);
        plannedOutputFolderCaptionLabel.Text = "Output folder";
        plannedOutputFolderValueLabel.Text = "No run planned yet.";
        summaryTextBox.Text = "Choose the source data folder, select an output folder, choose the data to export, then run the migration.";
        standardModeRadioButton.Checked = true;

        RefreshPlannedOutputs();
        RefreshExportAvailability();
        UpdateGuidedStep();
    }

    private void BuildLayout()
    {
        var version = GetDisplayVersion();
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(244, 246, 248);
        ClientSize = new Size(980, 680);
        MinimumSize = new Size(900, 620);
        StartPosition = FormStartPosition.CenterScreen;
        Text = $"Spirits to BottlePOS Migration Utility {version} (.NET)";

        titleLabel.AutoSize = true;
        titleLabel.Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold);
        titleLabel.Text = $"Spirits to BottlePOS Migration Utility {version}";

        subtitleLabel.AutoSize = true;
        subtitleLabel.ForeColor = Color.FromArgb(83, 97, 113);
        subtitleLabel.Text = "Convert Spirits POS .dbf data to BottlePOS-ready CSV files.";

        ConfigureModeBar();

        headerLayout.AutoSize = true;
        headerLayout.ColumnCount = 1;
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headerLayout.Controls.Add(titleLabel, 0, 0);
        headerLayout.Controls.Add(subtitleLabel, 0, 1);
        headerLayout.Controls.Add(modeBar, 0, 2);
        headerLayout.Dock = DockStyle.Fill;
        headerLayout.Margin = Padding.Empty;
        headerLayout.RowCount = 3;
        headerLayout.RowStyles.Add(new RowStyle());
        headerLayout.RowStyles.Add(new RowStyle());
        headerLayout.RowStyles.Add(new RowStyle());

        ConfigureDirectories();
        ConfigureStatus();
        ConfigureExports();
        ConfigurePlannedOutputs();
        ConfigureGuidedMode();
        ConfigureButtons();
        ConfigureStandardLayout();

        contentPanel.Dock = DockStyle.Fill;
        contentPanel.Margin = new Padding(0, 12, 0, 0);
        contentPanel.Controls.Add(mainLayout);
        contentPanel.Controls.Add(guidedLayout);

        rootLayout.ColumnCount = 1;
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.Controls.Add(headerLayout, 0, 0);
        rootLayout.Controls.Add(contentPanel, 0, 1);
        rootLayout.Controls.Add(buttonBar, 0, 2);
        rootLayout.Dock = DockStyle.Fill;
        rootLayout.Padding = new Padding(14);
        rootLayout.RowCount = 3;
        rootLayout.RowStyles.Add(new RowStyle());
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle());

        Controls.Add(rootLayout);
    }

    private void ConfigureModeBar()
    {
        ConfigureRadioButton(standardModeRadioButton, "STANDARD");
        ConfigureRadioButton(guidedModeRadioButton, "GUIDED");

        modeBar.AutoSize = true;
        modeBar.Controls.Add(standardModeRadioButton);
        modeBar.Controls.Add(guidedModeRadioButton);
        modeBar.Margin = new Padding(0, 10, 0, 0);
    }

    private void ConfigureStandardLayout()
    {
        leftLayout.ColumnCount = 1;
        leftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        leftLayout.Controls.Add(directoriesGroupBox, 0, 0);
        leftLayout.Controls.Add(statusGroupBox, 0, 1);
        leftLayout.Dock = DockStyle.Fill;
        leftLayout.Margin = new Padding(0, 0, 8, 0);
        leftLayout.RowCount = 2;
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        rightLayout.ColumnCount = 1;
        rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rightLayout.Controls.Add(exportsGroupBox, 0, 0);
        rightLayout.Controls.Add(plannedOutputsGroupBox, 0, 1);
        rightLayout.Dock = DockStyle.Fill;
        rightLayout.Margin = new Padding(8, 0, 0, 0);
        rightLayout.RowCount = 2;
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        mainLayout.ColumnCount = 2;
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52F));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));
        mainLayout.Controls.Add(leftLayout, 0, 0);
        mainLayout.Controls.Add(rightLayout, 1, 0);
        mainLayout.Dock = DockStyle.Fill;
        mainLayout.Margin = Padding.Empty;
        mainLayout.RowCount = 1;
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
    }

    private void ConfigureDirectories()
    {
        directoriesGroupBox.Dock = DockStyle.Fill;
        directoriesGroupBox.Padding = new Padding(12);
        directoriesGroupBox.Text = "Directories";

        sourceDirectoryLabel.Anchor = AnchorStyles.Left;
        sourceDirectoryLabel.AutoSize = true;
        sourceDirectoryLabel.Text = "Spirits Data Directory:";

        sourceDirectoryTextBox.Dock = DockStyle.Fill;
        sourceDirectoryTextBox.PlaceholderText = "Choose the Spirits/KSV data directory";

        browseSourceButton.AutoSize = true;
        browseSourceButton.Text = "Browse";

        outputDirectoryLabel.Anchor = AnchorStyles.Left;
        outputDirectoryLabel.AutoSize = true;
        outputDirectoryLabel.Text = "Output File Directory:";

        outputDirectoryTextBox.Dock = DockStyle.Fill;
        outputDirectoryTextBox.PlaceholderText = "Choose where the CSV output folder should be created";

        browseOutputButton.AutoSize = true;
        browseOutputButton.Text = "Browse";

        directoryHintLabel.AutoSize = true;
        directoryHintLabel.ForeColor = Color.FromArgb(83, 97, 113);
        directoryHintLabel.Text = "The output folder is remembered on this Windows user profile.";

        directoriesLayout.ColumnCount = 3;
        directoriesLayout.ColumnStyles.Add(new ColumnStyle());
        directoriesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        directoriesLayout.ColumnStyles.Add(new ColumnStyle());
        directoriesLayout.Controls.Add(sourceDirectoryLabel, 0, 0);
        directoriesLayout.Controls.Add(sourceDirectoryTextBox, 1, 0);
        directoriesLayout.Controls.Add(browseSourceButton, 2, 0);
        directoriesLayout.Controls.Add(outputDirectoryLabel, 0, 1);
        directoriesLayout.Controls.Add(outputDirectoryTextBox, 1, 1);
        directoriesLayout.Controls.Add(browseOutputButton, 2, 1);
        directoriesLayout.Controls.Add(directoryHintLabel, 0, 2);
        directoriesLayout.Dock = DockStyle.Fill;
        directoriesLayout.RowCount = 3;
        directoriesLayout.RowStyles.Add(new RowStyle());
        directoriesLayout.RowStyles.Add(new RowStyle());
        directoriesLayout.RowStyles.Add(new RowStyle());
        directoriesLayout.SetColumnSpan(directoryHintLabel, 3);

        directoriesGroupBox.Controls.Add(directoriesLayout);
    }

    private void ConfigureStatus()
    {
        statusGroupBox.Dock = DockStyle.Fill;
        statusGroupBox.Padding = new Padding(12);
        statusGroupBox.Text = "Run Status";

        statusCaptionLabel.Anchor = AnchorStyles.Left;
        statusCaptionLabel.AutoSize = true;
        statusCaptionLabel.Text = "Status";

        statusValueLabel.Anchor = AnchorStyles.Left;
        statusValueLabel.AutoSize = true;

        migrationProgressBar.Dock = DockStyle.Fill;
        migrationProgressBar.Maximum = 100;

        plannedOutputFolderCaptionLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        plannedOutputFolderCaptionLabel.AutoSize = true;
        plannedOutputFolderCaptionLabel.Text = "Output folder";

        plannedOutputFolderValueLabel.AutoEllipsis = true;
        plannedOutputFolderValueLabel.Dock = DockStyle.Fill;
        plannedOutputFolderValueLabel.ForeColor = Color.FromArgb(83, 97, 113);

        summaryTextBox.Dock = DockStyle.Fill;
        summaryTextBox.Multiline = true;
        summaryTextBox.ReadOnly = true;
        summaryTextBox.ScrollBars = ScrollBars.Vertical;
        summaryTextBox.Font = new Font("Consolas", 9F);

        statusLayout.ColumnCount = 2;
        statusLayout.ColumnStyles.Add(new ColumnStyle());
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        statusLayout.Controls.Add(statusCaptionLabel, 0, 0);
        statusLayout.Controls.Add(statusValueLabel, 1, 0);
        statusLayout.Controls.Add(migrationProgressBar, 0, 1);
        statusLayout.Controls.Add(plannedOutputFolderCaptionLabel, 0, 2);
        statusLayout.Controls.Add(plannedOutputFolderValueLabel, 1, 2);
        statusLayout.Controls.Add(summaryTextBox, 0, 3);
        statusLayout.Dock = DockStyle.Fill;
        statusLayout.RowCount = 4;
        statusLayout.RowStyles.Add(new RowStyle());
        statusLayout.RowStyles.Add(new RowStyle());
        statusLayout.RowStyles.Add(new RowStyle());
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        statusLayout.SetColumnSpan(migrationProgressBar, 2);
        statusLayout.SetColumnSpan(summaryTextBox, 2);

        statusGroupBox.Controls.Add(statusLayout);
    }

    private void ConfigureExports()
    {
        exportsGroupBox.AutoSize = true;
        exportsGroupBox.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        exportsGroupBox.Dock = DockStyle.Fill;
        exportsGroupBox.MinimumSize = new Size(0, 205);
        exportsGroupBox.Padding = new Padding(12);
        exportsGroupBox.Text = "Table Selection";

        ConfigureCheckBox(departmentsCheckBox, "Department");
        ConfigureCheckBox(vendorsCheckBox, "Vendor");
        ConfigureCheckBox(customersCheckBox, "Customer");
        ConfigureCheckBox(inventoryCheckBox, "Inventory");
        ConfigureCheckBox(giftCardsCheckBox, "Gift Card");
        ConfigureCheckBox(includeInactiveCheckBox, "Include Inactive Products");
        ConfigureCheckBox(addQtyOneIfMissingCheckBox, "Add QTY=1 If Missing");
        ConfigureCheckBox(defaultPriceLevelCheckBox, "Use Price Level");

        defaultPriceLevelComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        defaultPriceLevelComboBox.Items.AddRange(["1", "2", "3"]);
        defaultPriceLevelComboBox.Width = 72;

        defaultPriceLevelHintLabel.AutoSize = true;
        defaultPriceLevelHintLabel.ForeColor = Color.FromArgb(83, 97, 113);
        defaultPriceLevelHintLabel.Text = "Price levels 1, 2, or 3";

        exportsLayout.AutoSize = true;
        exportsLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        exportsLayout.ColumnCount = 2;
        exportsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        exportsLayout.ColumnStyles.Add(new ColumnStyle());
        exportsLayout.Controls.Add(departmentsCheckBox, 0, 0);
        exportsLayout.Controls.Add(vendorsCheckBox, 0, 1);
        exportsLayout.Controls.Add(customersCheckBox, 0, 2);
        exportsLayout.Controls.Add(inventoryCheckBox, 0, 3);
        exportsLayout.Controls.Add(giftCardsCheckBox, 0, 4);
        exportsLayout.Controls.Add(includeInactiveCheckBox, 0, 5);
        exportsLayout.Controls.Add(addQtyOneIfMissingCheckBox, 0, 6);
        exportsLayout.Controls.Add(defaultPriceLevelCheckBox, 0, 7);
        exportsLayout.Controls.Add(defaultPriceLevelComboBox, 1, 7);
        exportsLayout.Controls.Add(defaultPriceLevelHintLabel, 0, 8);
        exportsLayout.Dock = DockStyle.Fill;
        exportsLayout.Margin = Padding.Empty;
        exportsLayout.RowCount = 9;
        for (var index = 0; index < 9; index++)
        {
            exportsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        exportsLayout.SetColumnSpan(includeInactiveCheckBox, 2);
        exportsLayout.SetColumnSpan(addQtyOneIfMissingCheckBox, 2);
        exportsLayout.SetColumnSpan(defaultPriceLevelHintLabel, 2);

        exportsGroupBox.Controls.Add(exportsLayout);
    }

    private void ConfigurePlannedOutputs()
    {
        plannedOutputsGroupBox.Dock = DockStyle.Fill;
        plannedOutputsGroupBox.Padding = new Padding(12);
        plannedOutputsGroupBox.Text = "Output Files";

        plannedOutputsHintLabel.AutoSize = true;
        plannedOutputsHintLabel.ForeColor = Color.FromArgb(83, 97, 113);
        plannedOutputsHintLabel.Text = "These filenames update as you change the export selection.";

        plannedOutputsListBox.Dock = DockStyle.Fill;
        plannedOutputsListBox.Font = new Font("Consolas", 9.5F);
        plannedOutputsListBox.HorizontalScrollbar = true;

        plannedOutputsLayout.ColumnCount = 1;
        plannedOutputsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        plannedOutputsLayout.Controls.Add(plannedOutputsHintLabel, 0, 0);
        plannedOutputsLayout.Controls.Add(plannedOutputsListBox, 0, 1);
        plannedOutputsLayout.Dock = DockStyle.Fill;
        plannedOutputsLayout.RowCount = 2;
        plannedOutputsLayout.RowStyles.Add(new RowStyle());
        plannedOutputsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        plannedOutputsGroupBox.Controls.Add(plannedOutputsLayout);
    }

    private void ConfigureGuidedMode()
    {
        guidedLayout.ColumnCount = 1;
        guidedLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        guidedLayout.Controls.Add(guidedStepLabel, 0, 0);
        guidedLayout.Controls.Add(guidedInstructionLabel, 0, 1);
        guidedLayout.Controls.Add(guidedContentPanel, 0, 2);
        guidedLayout.Controls.Add(guidedButtonBar, 0, 3);
        guidedLayout.Dock = DockStyle.Fill;
        guidedLayout.Margin = Padding.Empty;
        guidedLayout.RowCount = 4;
        guidedLayout.RowStyles.Add(new RowStyle());
        guidedLayout.RowStyles.Add(new RowStyle());
        guidedLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        guidedLayout.RowStyles.Add(new RowStyle());

        guidedStepLabel.AutoSize = true;
        guidedStepLabel.Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold);
        guidedInstructionLabel.AutoSize = true;
        guidedInstructionLabel.ForeColor = Color.FromArgb(83, 97, 113);
        guidedInstructionLabel.Margin = new Padding(0, 4, 0, 16);
        guidedContentPanel.Dock = DockStyle.Fill;

        ConfigureGuidedSourceStep();
        ConfigureGuidedOutputStep();
        ConfigureGuidedExportsStep();
        ConfigureGuidedInventoryOptionsStep();
        ConfigureGuidedRunStep();

        guidedBackButton.AutoSize = true;
        guidedBackButton.Padding = new Padding(14, 6, 14, 6);
        guidedBackButton.Text = "Back";

        guidedNextButton.AutoSize = true;
        guidedNextButton.BackColor = Color.FromArgb(32, 93, 158);
        guidedNextButton.ForeColor = Color.White;
        guidedNextButton.Padding = new Padding(14, 6, 14, 6);
        guidedNextButton.Text = "Next";
        guidedNextButton.UseVisualStyleBackColor = false;

        guidedRunButton.AutoSize = true;
        guidedRunButton.BackColor = Color.FromArgb(32, 93, 158);
        guidedRunButton.ForeColor = Color.White;
        guidedRunButton.Padding = new Padding(14, 6, 14, 6);
        guidedRunButton.Text = "Run";
        guidedRunButton.UseVisualStyleBackColor = false;

        guidedButtonBar.AutoSize = true;
        guidedButtonBar.Controls.Add(guidedRunButton);
        guidedButtonBar.Controls.Add(guidedNextButton);
        guidedButtonBar.Controls.Add(guidedBackButton);
        guidedButtonBar.Dock = DockStyle.Fill;
        guidedButtonBar.FlowDirection = FlowDirection.RightToLeft;
        guidedButtonBar.Margin = new Padding(0, 16, 0, 0);
    }

    private void ConfigureGuidedSourceStep()
    {
        ConfigureDirectoryStep(guidedSourceLayout, "Spirits Data Directory:", guidedSourceDirectoryTextBox, guidedBrowseSourceButton);
    }

    private void ConfigureGuidedOutputStep()
    {
        ConfigureDirectoryStep(guidedOutputLayout, "Output File Directory:", guidedOutputDirectoryTextBox, guidedBrowseOutputButton);
    }

    private static void ConfigureDirectoryStep(TableLayoutPanel layout, string labelText, TextBox textBox, Button browseButton)
    {
        var label = new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Text = labelText
        };

        textBox.Dock = DockStyle.Fill;
        browseButton.AutoSize = true;
        browseButton.Text = "Browse";

        layout.ColumnCount = 3;
        layout.ColumnStyles.Add(new ColumnStyle());
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle());
        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(textBox, 1, 0);
        layout.Controls.Add(browseButton, 2, 0);
        layout.Dock = DockStyle.Top;
        layout.RowCount = 1;
        layout.RowStyles.Add(new RowStyle());
    }

    private void ConfigureGuidedExportsStep()
    {
        ConfigureCheckBox(guidedDepartmentsCheckBox, "Department");
        ConfigureCheckBox(guidedVendorsCheckBox, "Vendor");
        ConfigureCheckBox(guidedCustomersCheckBox, "Customer");
        ConfigureCheckBox(guidedInventoryCheckBox, "Inventory");
        ConfigureCheckBox(guidedGiftCardsCheckBox, "Gift Card");

        guidedExportsLayout.AutoSize = true;
        guidedExportsLayout.ColumnCount = 1;
        guidedExportsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        guidedExportsLayout.Controls.Add(guidedDepartmentsCheckBox, 0, 0);
        guidedExportsLayout.Controls.Add(guidedVendorsCheckBox, 0, 1);
        guidedExportsLayout.Controls.Add(guidedCustomersCheckBox, 0, 2);
        guidedExportsLayout.Controls.Add(guidedInventoryCheckBox, 0, 3);
        guidedExportsLayout.Controls.Add(guidedGiftCardsCheckBox, 0, 4);
        guidedExportsLayout.Dock = DockStyle.Top;
        guidedExportsLayout.RowCount = 5;
        for (var index = 0; index < 5; index++)
        {
            guidedExportsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
    }

    private void ConfigureGuidedInventoryOptionsStep()
    {
        ConfigureCheckBox(guidedIncludeInactiveCheckBox, "Include Inactive Products");
        ConfigureCheckBox(guidedAddQtyOneIfMissingCheckBox, "Add QTY=1 If Missing");
        ConfigureCheckBox(guidedDefaultPriceLevelCheckBox, "Use Price Level");

        guidedDefaultPriceLevelComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        guidedDefaultPriceLevelComboBox.Items.AddRange(["1", "2", "3"]);
        guidedDefaultPriceLevelComboBox.Width = 72;

        guidedInventoryOptionsLayout.AutoSize = true;
        guidedInventoryOptionsLayout.ColumnCount = 2;
        guidedInventoryOptionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        guidedInventoryOptionsLayout.ColumnStyles.Add(new ColumnStyle());
        guidedInventoryOptionsLayout.Controls.Add(guidedIncludeInactiveCheckBox, 0, 0);
        guidedInventoryOptionsLayout.Controls.Add(guidedAddQtyOneIfMissingCheckBox, 0, 1);
        guidedInventoryOptionsLayout.Controls.Add(guidedDefaultPriceLevelCheckBox, 0, 2);
        guidedInventoryOptionsLayout.Controls.Add(guidedDefaultPriceLevelComboBox, 1, 2);
        guidedInventoryOptionsLayout.Dock = DockStyle.Top;
        guidedInventoryOptionsLayout.RowCount = 3;
        for (var index = 0; index < 3; index++)
        {
            guidedInventoryOptionsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        guidedInventoryOptionsLayout.SetColumnSpan(guidedIncludeInactiveCheckBox, 2);
        guidedInventoryOptionsLayout.SetColumnSpan(guidedAddQtyOneIfMissingCheckBox, 2);
    }

    private void ConfigureGuidedRunStep()
    {
        guidedRunSummaryLabel.AutoSize = true;
        guidedRunSummaryLabel.Dock = DockStyle.Top;
        guidedRunSummaryLabel.Font = new Font("Segoe UI", 10F);
        guidedRunSummaryLabel.MaximumSize = new Size(760, 0);

        guidedRunLayout.ColumnCount = 1;
        guidedRunLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        guidedRunLayout.Controls.Add(guidedRunSummaryLabel, 0, 0);
        guidedRunLayout.Dock = DockStyle.Top;
        guidedRunLayout.RowCount = 1;
        guidedRunLayout.RowStyles.Add(new RowStyle());
    }

    private void ConfigureButtons()
    {
        startButton.AutoSize = true;
        startButton.BackColor = Color.FromArgb(32, 93, 158);
        startButton.ForeColor = Color.White;
        startButton.Padding = new Padding(14, 6, 14, 6);
        startButton.Text = "&Process";
        startButton.UseVisualStyleBackColor = false;

        openZipFolderButton.AutoSize = true;
        openZipFolderButton.Enabled = false;
        openZipFolderButton.Padding = new Padding(14, 6, 14, 6);
        openZipFolderButton.Text = "Open ZIP Folder";
        openZipFolderButton.Visible = false;

        closeButton.AutoSize = true;
        closeButton.Padding = new Padding(14, 6, 14, 6);
        closeButton.Text = "&Finish";

        buttonBar.AutoSize = true;
        buttonBar.Controls.Add(closeButton);
        buttonBar.Controls.Add(openZipFolderButton);
        buttonBar.Controls.Add(startButton);
        buttonBar.Dock = DockStyle.Fill;
        buttonBar.FlowDirection = FlowDirection.RightToLeft;
        buttonBar.Margin = new Padding(0, 16, 0, 0);
    }

    private static void ConfigureCheckBox(CheckBox checkBox, string text)
    {
        checkBox.AutoSize = true;
        checkBox.Text = text;
    }

    private static void ConfigureRadioButton(RadioButton radioButton, string text)
    {
        radioButton.AutoSize = true;
        radioButton.Text = text;
    }

    private void BrowseForFolder(TextBox targetTextBox, string title)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(targetTextBox.Text)
                ? targetTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            targetTextBox.Text = dialog.SelectedPath;
        }
    }

    private void BrowseForOutputFolder(TextBox targetTextBox)
    {
        BrowseForFolder(targetTextBox, "Choose the destination for generated CSV files");
        _preferencesService.SaveLastOutputDirectory(targetTextBox.Text.Trim());
    }

    private async Task StartMigrationAsync()
    {
        if (_uiMode == UiMode.Guided && !ValidateGuidedRun())
        {
            return;
        }

        var request = BuildRequest();
        if (Directory.Exists(request.OutputDirectory))
        {
            _preferencesService.SaveLastOutputDirectory(request.OutputDirectory);
        }

        ToggleBusyState(true);
        migrationProgressBar.Value = 0;
        summaryTextBox.Clear();
        plannedOutputFolderCaptionLabel.Text = "Output folder";
        plannedOutputFolderValueLabel.Text = "Calculating...";

        try
        {
            var progress = new Progress<MigrationProgress>(update =>
            {
                migrationProgressBar.Value = Math.Clamp(update.Percent, migrationProgressBar.Minimum, migrationProgressBar.Maximum);
                statusValueLabel.Text = update.Message;
            });

            var result = await _migrationService.RunAsync(request, progress);
            ApplyResult(result);

            if (result.IsSuccess && !result.IsPreview)
            {
                ShowCompletionDialogAndClose(result);
            }
        }
        catch (Exception ex)
        {
            statusValueLabel.Text = "Migration stopped";
            statusValueLabel.ForeColor = Color.FromArgb(161, 40, 52);
            summaryTextBox.Text = $"The migration could not be completed:{Environment.NewLine}{Environment.NewLine}{ex.Message}";
        }
        finally
        {
            ToggleBusyState(false);
        }
    }

    private MigrationRequest BuildRequest()
    {
        return new MigrationRequest(
            GetCurrentSourceDirectory(),
            GetCurrentOutputDirectory(),
            GetCurrentOptions());
    }

    private ExportOptions GetCurrentOptions()
    {
        return _uiMode == UiMode.Guided
            ? new ExportOptions(
                guidedDepartmentsCheckBox.Checked,
                guidedVendorsCheckBox.Checked,
                guidedCustomersCheckBox.Checked,
                guidedInventoryCheckBox.Checked,
                guidedGiftCardsCheckBox.Checked,
                guidedIncludeInactiveCheckBox.Checked,
                guidedAddQtyOneIfMissingCheckBox.Checked,
                guidedDefaultPriceLevelCheckBox.Checked,
                guidedDefaultPriceLevelComboBox.SelectedItem?.ToString() ?? "1")
            : new ExportOptions(
                departmentsCheckBox.Checked,
                vendorsCheckBox.Checked,
                customersCheckBox.Checked,
                inventoryCheckBox.Checked,
                giftCardsCheckBox.Checked,
                includeInactiveCheckBox.Checked,
                addQtyOneIfMissingCheckBox.Checked,
                defaultPriceLevelCheckBox.Checked,
                defaultPriceLevelComboBox.SelectedItem?.ToString() ?? "1");
    }

    private string GetCurrentSourceDirectory()
    {
        return _uiMode == UiMode.Guided
            ? guidedSourceDirectoryTextBox.Text.Trim()
            : sourceDirectoryTextBox.Text.Trim();
    }

    private string GetCurrentOutputDirectory()
    {
        return _uiMode == UiMode.Guided
            ? guidedOutputDirectoryTextBox.Text.Trim()
            : outputDirectoryTextBox.Text.Trim();
    }

    private void ApplyResult(MigrationResult result)
    {
        statusValueLabel.ForeColor = result.IsSuccess
            ? Color.FromArgb(22, 101, 52)
            : Color.FromArgb(161, 40, 52);

        statusValueLabel.Text = result.IsSuccess
            ? result.IsPreview ? "Preview complete" : "Generation complete"
            : "Validation issues found";

        migrationProgressBar.Value = result.IsSuccess ? 100 : Math.Max(migrationProgressBar.Value, 1);
        plannedOutputFolderCaptionLabel.Text = result.IsSuccess && !result.IsPreview && !string.IsNullOrWhiteSpace(result.ZipFilePath)
            ? "ZIP archive"
            : "Output folder";
        plannedOutputFolderValueLabel.Text = result.IsSuccess && !result.IsPreview && !string.IsNullOrWhiteSpace(result.ZipFilePath)
            ? result.ZipFilePath
            : string.IsNullOrWhiteSpace(result.PlannedOutputDirectory)
                ? "No output folder planned."
                : result.PlannedOutputDirectory;

        summaryTextBox.Text = result.Summary;

        if (result.IsSuccess && !result.IsPreview)
        {
            _lastZipFilePath = result.ZipFilePath;
            openZipFolderButton.Visible = true;
            openZipFolderButton.Enabled = File.Exists(_lastZipFilePath);

            var createdFiles = result.CreatedFiles.ToList();
            if (!string.IsNullOrWhiteSpace(result.ZipFilePath))
            {
                createdFiles.Add(Path.GetFileName(result.ZipFilePath));
            }

            ReplacePlannedOutputs(createdFiles);
            return;
        }

        openZipFolderButton.Visible = false;
        openZipFolderButton.Enabled = false;
        ReplacePlannedOutputs(result.PlannedOutputs);
    }

    private void RefreshPlannedOutputs()
    {
        ReplacePlannedOutputs(MigrationCatalog.GetPlannedOutputs(GetCurrentOptions()));
    }

    private void ReplacePlannedOutputs(IReadOnlyList<string> plannedOutputs)
    {
        plannedOutputsListBox.BeginUpdate();
        plannedOutputsListBox.Items.Clear();

        if (plannedOutputs.Count == 0)
        {
            plannedOutputsListBox.Items.Add("Select at least one export to preview outputs.");
        }
        else
        {
            foreach (var output in plannedOutputs)
            {
                plannedOutputsListBox.Items.Add(output);
            }
        }

        plannedOutputsListBox.EndUpdate();
    }

    private void RefreshExportAvailability()
    {
        ApplyExportAvailability(
            sourceDirectoryTextBox.Text.Trim(),
            departmentsCheckBox,
            vendorsCheckBox,
            customersCheckBox,
            inventoryCheckBox,
            giftCardsCheckBox,
            includeInactiveCheckBox,
            addQtyOneIfMissingCheckBox,
            defaultPriceLevelCheckBox,
            defaultPriceLevelComboBox);

        ApplyExportAvailability(
            guidedSourceDirectoryTextBox.Text.Trim(),
            guidedDepartmentsCheckBox,
            guidedVendorsCheckBox,
            guidedCustomersCheckBox,
            guidedInventoryCheckBox,
            guidedGiftCardsCheckBox,
            guidedIncludeInactiveCheckBox,
            guidedAddQtyOneIfMissingCheckBox,
            guidedDefaultPriceLevelCheckBox,
            guidedDefaultPriceLevelComboBox);
    }

    private void ApplyExportAvailability(
        string sourceDirectory,
        CheckBox departments,
        CheckBox vendors,
        CheckBox customers,
        CheckBox inventory,
        CheckBox giftCards,
        CheckBox includeInactive,
        CheckBox addQtyOneIfMissing,
        CheckBox defaultPriceLevel,
        ComboBox defaultPriceLevelComboBox)
    {
        var hasSourceDirectory = Directory.Exists(sourceDirectory);
        var departmentsAvailable = !hasSourceDirectory || HasRequiredTables(sourceDirectory, MigrationCatalog.GetRequiredTablesForExport("departments"));
        var vendorsAvailable = !hasSourceDirectory || HasRequiredTables(sourceDirectory, MigrationCatalog.GetRequiredTablesForExport("vendors"));
        var customersAvailable = !hasSourceDirectory || HasRequiredTables(sourceDirectory, MigrationCatalog.GetRequiredTablesForExport("customers"));
        var inventoryAvailable = !hasSourceDirectory || HasRequiredTables(sourceDirectory, MigrationCatalog.GetRequiredTablesForExport("inventory"));
        var giftCardsAvailable = !hasSourceDirectory || HasRequiredTables(sourceDirectory, MigrationCatalog.GetRequiredTablesForExport("giftcards"));

        ApplyAvailability(departments, departmentsAvailable);
        ApplyAvailability(vendors, vendorsAvailable);
        ApplyAvailability(customers, customersAvailable);
        ApplyAvailability(inventory, inventoryAvailable);
        ApplyAvailability(giftCards, giftCardsAvailable);

        var inventoryOptionsAvailable = !_isBusy && inventoryAvailable && inventory.Checked;
        includeInactive.Enabled = inventoryOptionsAvailable;
        addQtyOneIfMissing.Enabled = inventoryOptionsAvailable;
        defaultPriceLevel.Enabled = inventoryOptionsAvailable;
        defaultPriceLevelComboBox.Enabled = inventoryOptionsAvailable && defaultPriceLevel.Checked;
    }

    private static bool HasRequiredTables(string sourceDirectory, IReadOnlyList<string> requiredTables)
    {
        return requiredTables.All(table => File.Exists(Path.Combine(sourceDirectory, table)));
    }

    private void ApplyAvailability(CheckBox checkBox, bool isAvailable)
    {
        if (!isAvailable)
        {
            checkBox.Checked = false;
        }

        checkBox.Enabled = !_isBusy && isAvailable;
    }

    private void ToggleBusyState(bool isBusy)
    {
        _isBusy = isBusy;
        startButton.Enabled = !isBusy;
        closeButton.Enabled = !isBusy;
        browseSourceButton.Enabled = !isBusy;
        browseOutputButton.Enabled = !isBusy;
        guidedBrowseSourceButton.Enabled = !isBusy;
        guidedBrowseOutputButton.Enabled = !isBusy;
        standardModeRadioButton.Enabled = !isBusy;
        guidedModeRadioButton.Enabled = !isBusy;
        guidedBackButton.Enabled = !isBusy && _guidedStep != GuidedStep.Source;
        guidedNextButton.Enabled = !isBusy;
        guidedRunButton.Enabled = !isBusy;
        RefreshExportAvailability();
        UpdateGuidedStep();
    }

    private void SetUiMode(UiMode mode)
    {
        if (_isChangingMode)
        {
            return;
        }

        _isChangingMode = true;
        try
        {
            if (_uiMode == UiMode.Guided && mode == UiMode.Standard)
            {
                SyncGuidedToStandard();
            }
            else if (_uiMode == UiMode.Standard && mode == UiMode.Guided)
            {
                SyncStandardToGuided();
            }

            _uiMode = mode;
            standardModeRadioButton.Checked = mode == UiMode.Standard;
            guidedModeRadioButton.Checked = mode == UiMode.Guided;
            mainLayout.Visible = mode == UiMode.Standard;
            guidedLayout.Visible = mode == UiMode.Guided;
            buttonBar.Visible = mode == UiMode.Standard;
            RefreshPlannedOutputs();
            RefreshExportAvailability();
            UpdateGuidedStep();
        }
        finally
        {
            _isChangingMode = false;
        }
    }

    private void SyncStandardToGuided()
    {
        guidedSourceDirectoryTextBox.Text = sourceDirectoryTextBox.Text;
        guidedOutputDirectoryTextBox.Text = outputDirectoryTextBox.Text;
        guidedDepartmentsCheckBox.Checked = departmentsCheckBox.Checked;
        guidedVendorsCheckBox.Checked = vendorsCheckBox.Checked;
        guidedCustomersCheckBox.Checked = customersCheckBox.Checked;
        guidedInventoryCheckBox.Checked = inventoryCheckBox.Checked;
        guidedGiftCardsCheckBox.Checked = giftCardsCheckBox.Checked;
        guidedIncludeInactiveCheckBox.Checked = includeInactiveCheckBox.Checked;
        guidedAddQtyOneIfMissingCheckBox.Checked = addQtyOneIfMissingCheckBox.Checked;
        guidedDefaultPriceLevelCheckBox.Checked = defaultPriceLevelCheckBox.Checked;
        guidedDefaultPriceLevelComboBox.SelectedItem = defaultPriceLevelComboBox.SelectedItem;
    }

    private void SyncGuidedToStandard()
    {
        sourceDirectoryTextBox.Text = guidedSourceDirectoryTextBox.Text;
        outputDirectoryTextBox.Text = guidedOutputDirectoryTextBox.Text;
        departmentsCheckBox.Checked = guidedDepartmentsCheckBox.Checked;
        vendorsCheckBox.Checked = guidedVendorsCheckBox.Checked;
        customersCheckBox.Checked = guidedCustomersCheckBox.Checked;
        inventoryCheckBox.Checked = guidedInventoryCheckBox.Checked;
        giftCardsCheckBox.Checked = guidedGiftCardsCheckBox.Checked;
        includeInactiveCheckBox.Checked = guidedIncludeInactiveCheckBox.Checked;
        addQtyOneIfMissingCheckBox.Checked = guidedAddQtyOneIfMissingCheckBox.Checked;
        defaultPriceLevelCheckBox.Checked = guidedDefaultPriceLevelCheckBox.Checked;
        defaultPriceLevelComboBox.SelectedItem = guidedDefaultPriceLevelComboBox.SelectedItem;
    }

    private void SyncOutputDirectory(TextBox source, TextBox destination)
    {
        if (destination.Text != source.Text)
        {
            destination.Text = source.Text;
        }
    }

    private void MoveGuidedNext()
    {
        if (!ValidateGuidedStep(_guidedStep))
        {
            return;
        }

        _guidedStep = _guidedStep switch
        {
            GuidedStep.Source => GuidedStep.Output,
            GuidedStep.Output => GuidedStep.Exports,
            GuidedStep.Exports => guidedInventoryCheckBox.Checked ? GuidedStep.InventoryOptions : GuidedStep.Run,
            GuidedStep.InventoryOptions => GuidedStep.Run,
            _ => GuidedStep.Run
        };

        UpdateGuidedStep();
    }

    private void MoveGuidedBack()
    {
        _guidedStep = _guidedStep switch
        {
            GuidedStep.Output => GuidedStep.Source,
            GuidedStep.Exports => GuidedStep.Output,
            GuidedStep.InventoryOptions => GuidedStep.Exports,
            GuidedStep.Run => guidedInventoryCheckBox.Checked ? GuidedStep.InventoryOptions : GuidedStep.Exports,
            _ => GuidedStep.Source
        };

        UpdateGuidedStep();
    }

    private void UpdateGuidedStep()
    {
        if (guidedContentPanel.IsDisposed)
        {
            return;
        }

        guidedContentPanel.Controls.Clear();

        switch (_guidedStep)
        {
            case GuidedStep.Source:
                guidedStepLabel.Text = "Step 1 of 5: Select Data Source";
                guidedInstructionLabel.Text = "Choose the Spirits/KSV data folder that contains the source DBF files.";
                guidedContentPanel.Controls.Add(guidedSourceLayout);
                break;
            case GuidedStep.Output:
                guidedStepLabel.Text = "Step 2 of 5: Select Export Location";
                guidedInstructionLabel.Text = "Choose where the generated ZIP archive should be created.";
                guidedContentPanel.Controls.Add(guidedOutputLayout);
                break;
            case GuidedStep.Exports:
                guidedStepLabel.Text = "Step 3 of 5: Select Data To Export";
                guidedInstructionLabel.Text = "Select the data sets to include in this migration.";
                guidedContentPanel.Controls.Add(guidedExportsLayout);
                break;
            case GuidedStep.InventoryOptions:
                guidedStepLabel.Text = "Step 4 of 5: Inventory Options";
                guidedInstructionLabel.Text = "Choose the inventory-specific options for this migration.";
                guidedContentPanel.Controls.Add(guidedInventoryOptionsLayout);
                break;
            case GuidedStep.Run:
                guidedStepLabel.Text = "Step 5 of 5: Run Migration";
                guidedInstructionLabel.Text = "Review the selections, then run the export.";
                guidedRunSummaryLabel.Text = BuildGuidedRunSummary();
                guidedContentPanel.Controls.Add(guidedRunLayout);
                break;
        }

        guidedBackButton.Enabled = !_isBusy && _guidedStep != GuidedStep.Source;
        guidedNextButton.Visible = _guidedStep != GuidedStep.Run;
        guidedRunButton.Visible = _guidedStep == GuidedStep.Run;
        guidedRunButton.Enabled = !_isBusy;
    }

    private bool ValidateGuidedStep(GuidedStep step)
    {
        if (step == GuidedStep.Source && !Directory.Exists(guidedSourceDirectoryTextBox.Text.Trim()))
        {
            MessageBox.Show(this, "Choose a valid Spirits/KSV data directory.", "Source Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        if (step == GuidedStep.Output && !Directory.Exists(guidedOutputDirectoryTextBox.Text.Trim()))
        {
            MessageBox.Show(this, "Choose a valid output directory.", "Output Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        if (step == GuidedStep.Exports && !GetCurrentOptions().HasSelections)
        {
            MessageBox.Show(this, "Select at least one export.", "Selection Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        return true;
    }

    private bool ValidateGuidedRun()
    {
        return ValidateGuidedStep(GuidedStep.Source) &&
               ValidateGuidedStep(GuidedStep.Output) &&
               ValidateGuidedStep(GuidedStep.Exports);
    }

    private string BuildGuidedRunSummary()
    {
        var options = GetCurrentOptions();
        var selectedExports = MigrationCatalog.GetSelectedExports(options);
        var builder = new StringBuilder();
        builder.AppendLine($"Source: {guidedSourceDirectoryTextBox.Text.Trim()}");
        builder.AppendLine($"Output: {guidedOutputDirectoryTextBox.Text.Trim()}");
        builder.AppendLine();
        builder.AppendLine("Exports:");

        foreach (var export in selectedExports)
        {
            builder.AppendLine($"- {export}");
        }

        if (options.ExportInventory)
        {
            builder.AppendLine();
            builder.AppendLine("Inventory:");
            builder.AppendLine($"- Include inactive products: {(options.IncludeInactiveProducts ? "Yes" : "No")}");
            builder.AppendLine($"- Add QTY=1 if missing: {(options.AddQuantityOneIfMissing ? "Yes" : "No")}");
            builder.AppendLine($"- Price level: {(options.UseDefaultPriceLevel ? options.DefaultPriceLevel : "All except 7, 8, 9")}");
        }

        return builder.ToString().TrimEnd();
    }

    private void OpenZipFolder()
    {
        if (string.IsNullOrWhiteSpace(_lastZipFilePath) || !File.Exists(_lastZipFilePath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastZipFilePath}\"")
        {
            UseShellExecute = true
        });
    }

    private void ShowCompletionDialogAndClose(MigrationResult result)
    {
        using var dialog = new Form
        {
            Text = "Migration Complete",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(560, 420),
            MinimizeBox = false,
            MaximizeBox = false
        };

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle());

        var title = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
            Text = "Export complete"
        };

        var report = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = BuildCompletionReport(result)
        };

        var finishButton = new Button
        {
            Anchor = AnchorStyles.Right,
            AutoSize = true,
            DialogResult = DialogResult.OK,
            Padding = new Padding(18, 7, 18, 7),
            Text = "FINISH"
        };

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(report, 0, 1);
        layout.Controls.Add(finishButton, 0, 2);
        dialog.AcceptButton = finishButton;
        dialog.Controls.Add(layout);
        dialog.ShowDialog(this);
        Close();
    }

    private static string BuildCompletionReport(MigrationResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("The migration finished successfully.");
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(result.ZipFilePath))
        {
            builder.AppendLine($"ZIP archive: {result.ZipFilePath}");
            builder.AppendLine();
        }

        builder.AppendLine("Files included:");
        foreach (var file in result.CreatedFiles)
        {
            builder.AppendLine($"- {file}");
        }

        builder.AppendLine();
        builder.AppendLine(result.Issues.Count == 0
            ? "Issues: none reported."
            : "Issues:");

        foreach (var issue in result.Issues)
        {
            builder.AppendLine($"- {issue}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string GetDisplayVersion()
    {
        var version = typeof(Form1).Assembly.GetName().Version;
        return version is null
            ? "Unknown"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private enum UiMode
    {
        Standard,
        Guided
    }

    private enum GuidedStep
    {
        Source,
        Output,
        Exports,
        InventoryOptions,
        Run
    }
}
