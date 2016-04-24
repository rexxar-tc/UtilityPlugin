using System.Collections.Generic;
using System.Text;
using SpaceEngineers.Game.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;

namespace UtilityPlugin
{
    public class LCDMenu
    {
        private Ingame.IMyTextPanel text;
        private IMyButtonPanel buttons;
        private MenuItem currentItem;
        private MenuItem rootMenu;
        private int selectedItemIndex = 0;

        public MenuItem Root { get { return rootMenu; } set { rootMenu = value; } }

        public LCDMenu()
        {
            rootMenu = new MenuItem("Root", null);
            rootMenu.root = this;
            SetCurrentItem(rootMenu);
        }

        public void BindButtonPanel(IMyButtonPanel btnpnl)
        {
            if (buttons != null)
            {
                buttons.ButtonPressed -= ButtonPanelHandler;
            }
            buttons = btnpnl;
            buttons.ButtonPressed += ButtonPanelHandler;
        }

        public void BindLCD(Ingame.IMyTextPanel txtpnl)
        {
            if (text != null)
            {
                text.WritePublicText("MENU UNBOUND");
            }
            text = txtpnl;
            UpdateLCD();
        }

        public void SetCurrentItem(MenuItem item)
        {
            currentItem = item;
        }

        public void ButtonPanelHandler(int button)
        {
            switch (button)
            {
                case 0:
                    if (selectedItemIndex > 0)
                    {
                        selectedItemIndex--;
                    }
                    break;
                case 1:
                    if (selectedItemIndex < currentItem.Items.Count - 1)
                    {
                        selectedItemIndex++;
                    }
                    break;
                case 2:
                    currentItem.Items[selectedItemIndex].Invoke();
                    break;
                case 3:
                    MenuActions.UpLevel(this, currentItem.Items[selectedItemIndex]);
                    break;

            }
            UpdateLCD();
        }

        public void UpdateLCD()
        {
            if (text != null)
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < currentItem.Items.Count; i++)
                {
                    if (i == selectedItemIndex)
                    {
                        sb.Append("| " + currentItem.Items[i].Name + '\n');
                    }
                    else
                    {
                        sb.Append("' " + currentItem.Items[i].Name + '\n');
                    }
                }

                text.WritePublicText(sb.ToString());
                text.ShowPrivateTextOnScreen();
                text.ShowPublicTextOnScreen();
            }
        }
    }

    public class MenuItem
    {
        public LCDMenu root;
        private string name;
        private MenuItem parent;
        private List<MenuItem> children;
        private MenuAction action;

        public delegate void MenuAction(LCDMenu root, MenuItem item);
        public MenuAction Action { get { return action; } set { action = value; } }
        public MenuItem Parent { get { return parent; } }
        public List<MenuItem> Items { get { return children; } set { children = value; } }
        public string Name { get { return name; } }

        public MenuItem(string name, MenuAction action)
        {
            this.name = name;
            this.action = action;
            this.children = new List<MenuItem>();
        }

        public void Add(MenuItem child)
        {
            child.root = root;
            child.parent = this;
            children.Add(child);
        }

        public void Invoke()
        {
            action.Invoke(root, this);
        }
    }

    public static class MenuActions
    {
        public static void DownLevel(LCDMenu root, MenuItem item)
        {
            root.SetCurrentItem(item);
        }

        public static void UpLevel(LCDMenu root, MenuItem item)
        {
            if (item.Parent.Parent != null)
            {
                root.SetCurrentItem(item.Parent.Parent);
            }
        }
    }
}