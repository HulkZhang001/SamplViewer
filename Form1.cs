using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Safe.FMEObjects;
using System.Collections.Specialized;

namespace SamplViewer
{
    public partial class Form1 : Form
    {
        private IFMEOSession m_fmeSession = null;
        private IFMEOGeometryTools m_fmeGeometryTools = null;
        private IFMEOLogFile m_fmeLogFile = null;
        private IFMEODialog m_fmeDialog = null;
        private FMEOFormatInfo m_dataInfo = null;
        private StringCollection m_createDirectives = null;
        private const int m_kUpdateInterval = 500;
        private SortedList<string, IFMEOFeatureVectorOnDisk> m_featureTypeDictionary = null;
        private const int m_kMaxFeatureInMemory = 500;
        private enum eTabPanels
        {
            FeatureTypeTab = 0,
            GeometryTab,
            CoordSysTab,
            FormatInfoTab,
            SchemaInfoTab
        }


        public Form1()
        {
            InitializeComponent();

            m_fmeSession = FMEObjects.CreateSession();
            m_fmeSession.Init(null);

            m_fmeGeometryTools = m_fmeSession.GeometryTools();

            m_fmeLogFile = m_fmeSession.LogFile();
            m_fmeLogFile.SetFileName("logfile.log", false);
            m_fmeDialog = m_fmeSession.CreateDialogBox();
            m_fmeDialog.SetParentWindow(this.Handle);

            m_dataInfo = new FMEOFormatInfo();
            m_createDirectives = new StringCollection();

            m_featureTypeDictionary = new SortedList<string, IFMEOFeatureVectorOnDisk>();
            // Disable the tab panels
            tabControl1.Enabled = false;



        }


        private void updateStatusBar(string pText)
        {
            toolStripStatusLabel1.Text = pText;
            statusStrip1.Refresh();
        }
        private void exitOption_Click(object sender, System.EventArgs e)
        {
            this.Dispose();
            Application.Exit();
        }

        private void aboutOption_Click(object sender, System.EventArgs e)
        {
            try
            {
                m_fmeDialog.About("Sample Application");
            }
            catch (FMEOException ex)
            {
                // Log errors to log file
                m_fmeLogFile.LogMessageString(ex.FmeErrorMessage,
                 FMEOMessageLevel.Error);
                m_fmeLogFile.LogMessageString(ex.FmeStackTrace,
                 FMEOMessageLevel.Error);
                m_fmeLogFile.LogMessageString(ex.FmeErrorNumber.ToString(),
                 FMEOMessageLevel.Error);

                // Now exit the application
                Application.Exit();
            }
        }
        private void openOption_Click(object sender, System.EventArgs e)
        {
            try
            {
                if (m_fmeDialog.SourcePrompt("", "", m_dataInfo, m_createDirectives))
                {
                    // Disposes and clears entries inside m_featureTypeDictionary
                    resetAllDictionaries();

                    // Add IFMEOReader code here
                    // Update status bar
                    updateStatusBar("Reading from source...");

                    // If we are here, then the user did not hit Cancel. Now let's
                    // create a reader to read in features. Caching is enabled for
                    // the reader to improve performance on subsequent re-reads.
                    IFMEOReader fmeReader = m_fmeSession.CreateReader(m_dataInfo.Format, false,
                   m_createDirectives);
                    // Open the reader
                    StringCollection openParams = new StringCollection();
                    fmeReader.Open(m_dataInfo.Dataset, openParams);

                    int featureCount = 0;
                    // Now, read in the data features
                    readDataFeatures(fmeReader, ref featureCount);
                    // Update status bar with final feature count
                    updateStatusBar("Total features: " + featureCount.ToString());

                    // Clean up reader
                    fmeReader.Close();
                    fmeReader.Dispose();
                    fmeReader = null;

                    // Refresh the current tab
                    refreshCurrentTab();

                    // Enable the tab panels
                    tabControl1.Enabled = true;
                }
            }
            catch (FMEOException ex)
            {
                // Log errors to log file
                m_fmeLogFile.LogMessageString(ex.FmeErrorMessage, FMEOMessageLevel.Error);
                m_fmeLogFile.LogMessageString(ex.FmeStackTrace, FMEOMessageLevel.Error);
                m_fmeLogFile.LogMessageString(ex.FmeErrorNumber.ToString(),
                FMEOMessageLevel.Error);

                // Now exit the application
                Application.Exit();
            }
        }
        private void readDataFeatures(IFMEOReader fmeReader, ref int featureCount)
        {
            // Initialize counters
            int numFeatures = 0;

            // Now, read all the different features
            IFMEOFeature fmeFeature = m_fmeSession.CreateFeature();
            while (fmeReader.Read(fmeFeature))
            {
                // Log the feature to the log file
                m_fmeLogFile.LogFeature(fmeFeature, FMEOMessageLevel.Inform, -1);

                // Increment feature count
                numFeatures++;

                // For every feature, we will put it into m_featureTypeDictionary
                insertIntoFeatureTypeDictionary(fmeFeature);

                // Check if we need to update status bar
                if ((numFeatures % m_kUpdateInterval) == 0)
                {
                    updateStatusBar("Read " + numFeatures.ToString() + " features...");
                }


                // Create a new feature for the next read
                fmeFeature = m_fmeSession.CreateFeature();

            }
            fmeFeature.Dispose();

            featureCount = numFeatures;
        }
        private void insertIntoFeatureTypeDictionary(IFMEOFeature pFeature)
        {
            string currFeatureType = pFeature.FeatureType;
            // First, we check for where to put the feature inside m_featureTypeDictionary
            // Check to see if an entry for currFeatureType already exists
            if (!m_featureTypeDictionary.ContainsKey(currFeatureType))
            {
                // We must create a new entry to the m_featureTypeDictionary
                IFMEOFeatureVectorOnDisk newVectorOnDisk =
                m_fmeSession.CreateFeatureVectorOnDisk(m_kMaxFeatureInMemory);
                // Add the new entry to the dictionary
                m_featureTypeDictionary.Add(currFeatureType, newVectorOnDisk);
            }
            // Now, we put the feature into the proper IFMEOFeatureVectorOnDisk
            IFMEOFeatureVectorOnDisk currVectorOnDisk = m_featureTypeDictionary[currFeatureType];
            currVectorOnDisk.Append(pFeature);
        }
        private void disposeFeatureTypeDictionaryEntries()
        {
            IEnumerator<KeyValuePair<string, IFMEOFeatureVectorOnDisk>> iterator =
            m_featureTypeDictionary.GetEnumerator();
            // Iterate through each entry m_featureTypeDictionary, and clear each
            // IFMEOFeatureVectorOnDisk
            while (iterator.MoveNext())
            {
                string currFeatureType = iterator.Current.Key;
                IFMEOFeatureVectorOnDisk currVectorOnDisk = iterator.Current.Value;
                // Calling Clear of IFMEOFeatureVectorOnDisk should also dispose each of the
                // features that it contains
                currVectorOnDisk.Clear();
                currVectorOnDisk.Dispose();
                currVectorOnDisk = null;

            }
            // Finally, call Clear of m_featureTypeDictionary
            m_featureTypeDictionary.Clear();
        }
        private void resetAllDictionaries()
        {
            // Dispose all the features inside of m_featureTypeDictionary
            disposeFeatureTypeDictionaryEntries();
        }
        private void refreshCurrentTab()
        {
            eTabPanels selectedPanel = (eTabPanels)tabControl1.SelectedIndex;
            // Update the corresponding tab
            switch (selectedPanel)
            {
                case eTabPanels.FeatureTypeTab:
                    updateFeatureTypeTab();
                    break;
                default:
                    break;
            }
        }
        private void tabControl1_SelectedIndexChanged(object sender, System.EventArgs e)
        {
            refreshCurrentTab();
        }
        private void updateFeatureTypeTab()
        {
            // Clear the current entries in the featureTypeView
            featureTypeView.Items.Clear();
            IEnumerator<KeyValuePair<string, IFMEOFeatureVectorOnDisk>> iterator =
             m_featureTypeDictionary.GetEnumerator();
            while (iterator.MoveNext())
            {
                string currFeatureType = iterator.Current.Key;
                IFMEOFeatureVectorOnDisk currVectorOnDisk =
                m_featureTypeDictionary[currFeatureType];
                // Create a new subItem list - it will be a row of data containing
                // the feature type and its associated count
                string[] subItemList = { currFeatureType, currVectorOnDisk.Count.ToString() };
                ListViewItem newItem = new ListViewItem(subItemList, -1);
                // Add the item to featureTypeView
                featureTypeView.Items.Add(newItem);
            }
        }


    }

}
