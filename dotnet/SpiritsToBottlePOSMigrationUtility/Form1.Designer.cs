namespace SpiritsToBottlePOSMigrationUtility;

partial class Form1
{
    private System.ComponentModel.IContainer components = null;

    private TableLayoutPanel rootLayout;
    private TableLayoutPanel headerLayout;
    private TableLayoutPanel mainLayout;
    private TableLayoutPanel leftLayout;
    private TableLayoutPanel rightLayout;
    private TableLayoutPanel directoriesLayout;
    private TableLayoutPanel statusLayout;
    private TableLayoutPanel exportsLayout;
    private TableLayoutPanel plannedOutputsLayout;
    private FlowLayoutPanel buttonBar;
    private Label titleLabel;
    private Label subtitleLabel;
    private GroupBox directoriesGroupBox;
    private Label sourceDirectoryLabel;
    private TextBox sourceDirectoryTextBox;
    private Button browseSourceButton;
    private Label outputDirectoryLabel;
    private TextBox outputDirectoryTextBox;
    private Button browseOutputButton;
    private Label directoryHintLabel;
    private GroupBox statusGroupBox;
    private Label statusCaptionLabel;
    private Label statusValueLabel;
    private ProgressBar migrationProgressBar;
    private Label plannedOutputFolderCaptionLabel;
    private Label plannedOutputFolderValueLabel;
    private TextBox summaryTextBox;
    private GroupBox exportsGroupBox;
    private CheckBox departmentsCheckBox;
    private CheckBox vendorsCheckBox;
    private CheckBox customersCheckBox;
    private CheckBox inventoryCheckBox;
    private CheckBox giftCardsCheckBox;
    private CheckBox includeInactiveCheckBox;
    private CheckBox addQtyOneIfMissingCheckBox;
    private CheckBox defaultPriceLevelCheckBox;
    private ComboBox defaultPriceLevelComboBox;
    private Label defaultPriceLevelHintLabel;
    private Label exportsHintLabel;
    private GroupBox plannedOutputsGroupBox;
    private Label plannedOutputsHintLabel;
    private ListBox plannedOutputsListBox;
    private Button startButton;
    private Button closeButton;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        rootLayout = new();
        headerLayout = new();
        mainLayout = new();
        leftLayout = new();
        rightLayout = new();
        directoriesLayout = new();
        statusLayout = new();
        exportsLayout = new();
        plannedOutputsLayout = new();
        buttonBar = new();
        titleLabel = new();
        subtitleLabel = new();
        directoriesGroupBox = new();
        sourceDirectoryLabel = new();
        sourceDirectoryTextBox = new();
        browseSourceButton = new();
        outputDirectoryLabel = new();
        outputDirectoryTextBox = new();
        browseOutputButton = new();
        directoryHintLabel = new();
        statusGroupBox = new();
        statusCaptionLabel = new();
        statusValueLabel = new();
        migrationProgressBar = new();
        plannedOutputFolderCaptionLabel = new();
        plannedOutputFolderValueLabel = new();
        summaryTextBox = new();
        exportsGroupBox = new();
        departmentsCheckBox = new();
        vendorsCheckBox = new();
        customersCheckBox = new();
        inventoryCheckBox = new();
        giftCardsCheckBox = new();
        includeInactiveCheckBox = new();
        addQtyOneIfMissingCheckBox = new();
        defaultPriceLevelCheckBox = new();
        defaultPriceLevelComboBox = new();
        defaultPriceLevelHintLabel = new();
        exportsHintLabel = new();
        plannedOutputsGroupBox = new();
        plannedOutputsHintLabel = new();
        plannedOutputsListBox = new();
        startButton = new();
        closeButton = new();

        SuspendLayout();
        BuildLayout();
        ResumeLayout(false);
    }
}
