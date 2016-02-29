using UtilityPlugin.Settings;
using UtilityPlugin.UtilityClasses;

namespace UtilityPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.IO;
    using System.Xml.Serialization;
    using global::UtilityPlugin.Settings;
    using global::UtilityPlugin.UtilityClasses;

    [Serializable]
    public class PluginSettings
    {
        #region Private Fields

        private float _grindMultiplier;
        private float _weldmultiplier;
        private string _serverChatName;

        private static PluginSettings _instance;
        private static bool _loading = false;

        #endregion

        #region Static Properties

        public static PluginSettings Instance
        {
            get
            {
                return _instance ?? (_instance = new PluginSettings( ));
            }
        }
        #endregion

        #region Properties

        public string ServerChatName
        {
            get
            {
                return _serverChatName;
            }
            set
            {
                _serverChatName = value;
                Save( );
            }
        }

        public float GrindMultiplier
        {
            get
            {
                return _grindMultiplier;
                
            }
            set
            {
                _grindMultiplier = value;
                Save();
            }
        }

        public float WeldMultiplier
        {
            get
            {
                return _weldmultiplier;
            }
            set
            {
                _weldmultiplier = value;
                Save();
            }
        }
        #endregion



        #region Constructor
        public PluginSettings( )
        {
            _grindMultiplier = 0.5f;
            _weldmultiplier = 0.5f;
            _serverChatName = "Server";
        }


        #endregion

        #region Loading and Saving

        /// <summary>
        /// Loads our settings
        /// </summary>
        public void Load( )
        {
            _loading = true;

            try
            {
                lock ( this )
                {
                    String fileName = UtilityPlugin.PluginPath + "Utility-Settings.xml";
                    if ( File.Exists( fileName ) )
                    {
                        using ( StreamReader reader = new StreamReader( fileName ) )
                        {
                            XmlSerializer x = new XmlSerializer( typeof( PluginSettings ) );
                            PluginSettings settings = (PluginSettings)x.Deserialize( reader );
                            reader.Close( );

                            _instance = settings;
                        }
                    }
                }
            }
            catch ( Exception ex )
            {
                UtilityPlugin.Log.Error( ex );
            }
            finally
            {
                _loading = false;
            }
        }

        /// <summary>
        /// Saves our settings
        /// </summary>
        public void Save( )
        {
            if ( _loading )
                return;

            try
            {
                lock ( this )
                {
                    String fileName = UtilityPlugin.PluginPath + "Utility-Settings.xml";
                    using ( StreamWriter writer = new StreamWriter( fileName ) )
                    {
                        XmlSerializer x = new XmlSerializer( typeof( PluginSettings ) );
                        x.Serialize( writer, _instance );
                        writer.Close( );
                    }
                }
            }
            catch ( Exception ex )
            {
                UtilityPlugin.Log.Error( ex );
            }
        }

        #endregion

        #region Events

        /// <summary>
        /// Triggered when items changes.  
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ItemsCollectionChanged( object sender, NotifyCollectionChangedEventArgs e )
        {
            Save( );
        }

        private void OnPropertyChanged( object sender, PropertyChangedEventArgs e )
        {
            Console.WriteLine( "PropertyChanged()" );
            Save( );
        }

        #endregion
    }
}
