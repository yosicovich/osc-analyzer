using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Windows.Forms.DataVisualization.Charting;
using System.Runtime.InteropServices;
using System.Collections;
using LpgSys;

namespace OSCAnalyzer
{
    public partial class Form1 : Form
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct TOscRecord 
        {
            public short m_revolutions;
            public short m_lpgTime;
            public short m_petTime;
            public short m_tVap;
            public short m_tLpg;
            public short m_difPress;
            public short m_lambda;
            public short m_manPress;
        }
        Stream m_osc;

        double m_minMapToCount = 20;
        double m_maxMapToCount = 110;
        double m_calcMaxTimeDiff = 0.15;
        double m_selStart = 0;
        double m_selEnd = 0;
        double m_skipTimePoints = 250;
        double m_refDiffPress = 123;
        double m_refPress = 140;
        double m_refTemp = 600;
        double m_lambdaDiffPerc = 0.3;
        int m_lambdaMaxCount = 10;
        double m_offsetMin = -8;
        double m_offsetMax = 8;
        double m_offsetExactRange = 0.2;
        double m_offsetRoughStep = 0.1;
        double m_offsetExactStep = 0.01;
        int m_maxMap = 150;
        bool m_diffAutoCalc = false;
        DataPoint c1CurSelect = null;
        DataPoint c2CurSelect = null;
        DataPoint c3CurSelect = null;
        DataPoint c4CurSelect = null;
        Hashtable m_oscData = new Hashtable();
        Hashtable m_petData = new Hashtable();
        Hashtable m_lpgData = new Hashtable();
        long m_records = 0;

        public Form1()
        {
            InitializeComponent();
            comboBox1.SelectedIndex = 0;
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (openFileDialog1.ShowDialog() != DialogResult.OK)
                return;
            m_osc = openFileDialog1.OpenFile();
            chart1.Titles["MainTitle"].Text = openFileDialog1.FileName;
            loadOsc();
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private short convertPreasure(double calibrVal, short src)
        {
            if (!checkBox4.Checked)
                return src;
            return LpgSysCalcs.calcTamonaPress(calibrVal, src);
        }
        private void loadOsc()
        {
            // Clear            
            foreach (Series sr in chart1.Series )
            {
                sr.Points.Clear();
            }
            c1CurSelect = null;
            c2CurSelect = null;
            c3CurSelect = null;
            c4CurSelect = null;
            long len = m_osc.Length;
            int StructSize = Marshal.SizeOf(typeof(TOscRecord));
            if (len % StructSize != 0)
            {

                throw new Exception("Incorrect file size.");
            }
            double mapCalibr = System.Convert.ToDouble(numericUpDown16.Value);
            long Records = len / StructSize;
            long Record = 0;
            m_records = Records;

            m_oscData = new Hashtable();//.Clear();
            BinaryReader FS = new BinaryReader(m_osc);
            byte[] Buffer = new byte[StructSize];
            do
            {
                TOscRecord oscRec = new TOscRecord();
                Buffer = FS.ReadBytes(StructSize);
                ByteToStruct(Buffer, ref oscRec);
                // Correct data here
                short lpgPress = convertPreasure(mapCalibr, (short)(oscRec.m_manPress + oscRec.m_difPress));
                oscRec.m_manPress = convertPreasure(mapCalibr, oscRec.m_manPress);
                oscRec.m_difPress = (short)(lpgPress - oscRec.m_manPress);
                chart1.Series[0].Points.AddXY(Record, oscRec.m_petTime);
                chart1.Series[1].Points.AddXY(Record, oscRec.m_lpgTime);
                chart1.Series[2].Points.AddXY(Record, oscRec.m_manPress);
                chart1.Series[3].Points.AddXY(Record, oscRec.m_difPress);
                chart1.Series[4].Points.AddXY(Record, lpgPress);
                chart1.Series[5].Points.AddXY(Record, oscRec.m_tVap );
                chart1.Series[6].Points.AddXY(Record, oscRec.m_tLpg );
                chart1.Series[7].Points.AddXY(Record, oscRec.m_revolutions);
                chart1.Series[8].Points.AddXY(Record, oscRec.m_lambda);
                m_oscData.Add(System.Convert.ToDouble(Record), oscRec);
                Record += 1;
            } while (Record < Records);
            FS.Close();
            chart1.ChartAreas["MainArea"].CursorX.IsUserEnabled = true;
            chart1.ChartAreas["MainArea"].CursorX.IsUserSelectionEnabled = true;
            chart1.ChartAreas["MainArea"].CursorY.IsUserEnabled = true;
            chart1.ChartAreas["MainArea"].CursorY.IsUserSelectionEnabled = true;
            button2.Enabled = true;
            button4.Enabled = true;
            button5.Enabled = true;

        }
        // convert byte array ( record ) to structure
        private void ByteToStruct(byte[] Buffer, ref TOscRecord Struct)
        {
            IntPtr pCurrentPosition;
            GCHandle Handle = GCHandle.Alloc(Buffer, GCHandleType.Pinned);
            pCurrentPosition = Handle.AddrOfPinnedObject();
            Struct = (TOscRecord)(Marshal.PtrToStructure(pCurrentPosition,
            typeof(TOscRecord)));
            Handle.Free();
        }

        private void fillHashTable(Hashtable ht,DataPointCollection col)
        {
            foreach (DataPoint dp in col)
            {
                if (dp.IsEmpty)
                    continue;
                ArrayList list;
                if (!ht.ContainsKey(dp.XValue))
                {
                    list = new ArrayList();
                    ht.Add(dp.XValue, list);
                }
                else
                {
                    list = (ArrayList)ht[dp.XValue];
                }
                list.Add(dp.YValues[0]);
            }
        }

        private void addTable(Hashtable src, Hashtable dst)
        {
            if (src.Count == 0)
                return;
            foreach (DictionaryEntry de in src)
            {
                if (dst.ContainsKey(de.Key))
                {
                    ((ArrayList)dst[de.Key]).AddRange((ArrayList)de.Value);

                }
                else
                {
                    dst.Add(de.Key, de.Value);
                }
            }

        }

        private void calcGoodAvgHashTable(Hashtable ht, DataPointCollection col, double takeOutPerc, bool bDirect, double skipifLess)
        {
            foreach (DictionaryEntry de in ht)
            {
                double avg = 0;
                bool bFirst = true;
                foreach (double val in (ArrayList)de.Value)
                {
                    if (bFirst)
                    {
                        avg = val;
                        bFirst = false;
                        continue;
                    }
                    avg = (avg + val) / 2;
                }
                double perc = Math.Abs(avg * takeOutPerc);
                if (bDirect)
                    perc = takeOutPerc;
                double newAvg = 0;
                bFirst = true;
                foreach (double val in (ArrayList)de.Value)
                {
                    if (Math.Abs(Math.Abs(avg) - Math.Abs(val)) > perc)
                        continue; // Value is to far from average
                    if (bFirst)
                    {
                        newAvg = val;
                        bFirst = false;
                        continue;
                    }
                    newAvg = (newAvg + val) / 2;
                }
                if (newAvg < skipifLess)
                    continue;
                col.AddXY(de.Key, newAvg);
            }

        }

        private void processHashTableForPet(Hashtable ht, DataPointCollection col, double takeOutPerc, bool bDirect, double skipifLess, double offset, double factor)
        {
            foreach (DictionaryEntry de in ht)
            {
                double avg = 0;
                bool bFirst = true;
                foreach (TOscRecord val in (ArrayList)de.Value)
                {
                    if (bFirst)
                    {
                        avg = val.m_petTime;
                        bFirst = false;
                        continue;
                    }
                    avg = (avg + val.m_petTime) / 2;
                }
                double perc = Math.Abs(avg * takeOutPerc);
                if (bDirect)
                    perc = takeOutPerc;
                double newAvg = 0;
                bFirst = true;
                foreach (TOscRecord val in (ArrayList)de.Value)
                {
                    if (Math.Abs(Math.Abs(avg) - Math.Abs(val.m_petTime)) > perc)
                        continue; // Value is to far from average
                    if (bFirst)
                    {
                        newAvg = val.m_petTime;
                        bFirst = false;
                        continue;
                    }
                    newAvg = (newAvg + val.m_petTime) / 2;
                }
                if (newAvg < skipifLess)
                    continue;
                if(factor != 0 && offset != 0)
                {
                    double t = factor * ((double)de.Key) + offset * 100;
                    double pc = Math.Abs(t * takeOutPerc);
                    if (bDirect)
                        pc = takeOutPerc;
                    if (Math.Abs(t - newAvg) >= pc)
                        continue;
                }
                col.AddXY(de.Key, newAvg);
            }
        }

        private void processHashTableForLpg(Hashtable ht, DataPointCollection col, double takeOutPerc, bool bDirect, double skipifLess, double offset, double factor)
        {
            foreach (DictionaryEntry de in ht)
            {
                double avg = 0;
                bool bFirst = true;
                foreach (TOscRecord rec in (ArrayList)de.Value)
                {
                    double val = rec.m_lpgTime - offset*100;
                    val = val * Math.Sqrt((m_refTemp + 2731.5) / (rec.m_tLpg + 2731.5));
/*                    if(factor != 0 && offset != 0)
                        chart4.Series["lpgTime"].Points.AddXY(de.Key, val + offset * 100);*/

                    val = val * ((rec.m_difPress + rec.m_manPress) / m_refPress);
                    val = val + offset * 100;

                    if (m_diffAutoCalc)
                    {
                        if (bFirst)
                        {
                            m_refDiffPress = rec.m_difPress;
                        }
                        else
                        {
                            m_refDiffPress = (rec.m_difPress + m_refDiffPress) / 2;
                        }

                    }

                    if (bFirst)
                    {
                        avg = val;
                        bFirst = false;
                        continue;
                    }
                    avg = (avg + val) / 2;
                }
                double perc = Math.Abs(avg * takeOutPerc);
                if (bDirect)
                    perc = takeOutPerc;
                double newAvg = 0;
                bFirst = true;
                foreach (TOscRecord rec in (ArrayList)de.Value)
                {
                    double val = rec.m_lpgTime - offset*100;
                    val = val * ((rec.m_difPress + rec.m_manPress) / m_refPress);
                    val = val * Math.Sqrt((m_refTemp + 2731.5) / (rec.m_tLpg + 2731.5));
                    val = val + offset * 100;
                    if (Math.Abs(Math.Abs(avg) - Math.Abs(val)) > perc)
                        continue; // Value is to far from average
                    if (bFirst)
                    {
                        newAvg = val;
                        bFirst = false;
                        continue;
                    }
                    newAvg = (newAvg + val) / 2;
                }
                if (newAvg < skipifLess)
                    continue;
                if (factor != 0 && offset != 0)
                {
                    double t = factor * ((double)de.Key) + offset * 100;
                    double pc = Math.Abs(t * takeOutPerc);
                    if (bDirect)
                        pc = takeOutPerc;
                    if (Math.Abs(t - newAvg) >= pc)
                        continue;
                }
                col.AddXY(de.Key, newAvg);
            }
            if (m_diffAutoCalc)
            {
                numericUpDown1.Value = System.Convert.ToDecimal(m_refDiffPress);
            }

        }

        private void processHashTable2(Hashtable ht, DataPointCollection col)
        {
            foreach (DictionaryEntry de in ht)
            {
                foreach (double val in (ArrayList)de.Value)
                {
                    if (val < m_skipTimePoints)
                        continue;
                    col.AddXY(de.Key, val);
                }
            }

        }

        private void fillSecondChart(double selStart, double selEnd)
        {
            m_minMapToCount = System.Convert.ToDouble(numericUpDown2.Value);
            m_maxMapToCount = System.Convert.ToDouble(numericUpDown3.Value);
            m_calcMaxTimeDiff = System.Convert.ToDouble(numericUpDown4.Value)/100;
            m_skipTimePoints = System.Convert.ToDouble(numericUpDown5.Value)*100;
            m_refDiffPress = System.Convert.ToDouble(numericUpDown1.Value);
            m_refPress = System.Convert.ToDouble(numericUpDown6.Value);
            m_refTemp = System.Convert.ToDouble(numericUpDown7.Value)*10;
            m_lambdaDiffPerc = System.Convert.ToDouble(numericUpDown8.Value)/100;
            m_lambdaMaxCount = System.Convert.ToInt32(numericUpDown9.Value);
            m_offsetMax = System.Convert.ToDouble(numericUpDown10.Value);
            m_offsetMin = -m_offsetMax;
            m_maxMap = System.Convert.ToInt32(numericUpDown11.Value);
            m_diffAutoCalc = checkBox2.Checked;
            // setup parameters

            if (selStart > selEnd)
            {
                double temp;
                temp = selStart;
                selStart = selEnd;
                selEnd = temp;
            }
            Hashtable filterPet = new Hashtable();
            Hashtable filterLpg = new Hashtable();
            double lambda = 0;
            int lambdaCount = 0;
            foreach (DictionaryEntry de in m_oscData)
            {
                if ((double)de.Key < selStart || (double)de.Key > selEnd)
                    continue;
                double xVal = ((TOscRecord)de.Value).m_manPress;
                if (xVal < m_minMapToCount || xVal > m_maxMapToCount)
                    continue;
                if ((Math.Abs(((TOscRecord)de.Value).m_lambda - lambda) > lambda * m_lambdaDiffPerc) || checkBox5.Checked)
                {
                    lambda = ((TOscRecord)de.Value).m_lambda;
                    lambdaCount = 0;
                    addTable(filterPet, m_petData);
                    addTable(filterLpg, m_lpgData);
                    filterPet.Clear();
                    filterLpg.Clear();
                }
                else
                {
                    lambdaCount++;
                }
                if (lambdaCount > m_lambdaMaxCount)
                {
                    filterPet.Clear();
                    filterLpg.Clear();
                    continue;
                }
                ArrayList list;
                Hashtable curTable = null;
                double workTime = 0;
                if (((TOscRecord)de.Value).m_lpgTime != 0)
                {
                    curTable = filterLpg;
                    workTime = ((TOscRecord)de.Value).m_lpgTime;
                }
                else
                {
                    curTable = filterPet;
                    workTime = ((TOscRecord)de.Value).m_petTime;
                }
                if (!curTable.ContainsKey(xVal))
                {
                    list = new ArrayList();
                    curTable.Add(xVal, list);
                }
                else
                {
                    list = (ArrayList)curTable[xVal];
                }
                if (workTime < m_skipTimePoints)
                    continue;
                list.Add((TOscRecord)de.Value);
            }
            addTable(filterPet, m_petData);
            addTable(filterLpg, m_lpgData);

            double cleanPetfactor = 0;
            double petOffset = 0;
            calcAndDrawPet(ref cleanPetfactor, ref petOffset, false);

            double cleanLpgfactor = 0;
            double lpgOffset = 0;
            calcAndDrawLPG(ref cleanLpgfactor, ref lpgOffset, false);
            if (m_lpgData.Count != 0)
            {
/*                ArrayList[] ar = new ArrayList[2];
                ar[0] = new ArrayList();
                ar[1] = new ArrayList();
                for (int i = 0; i < m_maxMap; i++)
                {
                    for (int j = 100; j < m_maxMap + 150; j++)
                    {
                        double t = (cleanLpgfactor * i) + lpgOffset * 100;
                        t = (cleanLpgfactor * ((double)(m_refPress) / (j)) * i) + lpgOffset * 100;
                        //line1.Add(i, t);
                        double[] x = new double[3];
                        x[0] = i;
                        x[1] = t;
                        x[2] = j;
                        ar[0].Add(x);
                        surface1.Add(i, t, j);

                    }

                }
                // Chart data
                Super2d3dGraphLibrary.SeriesFactory sf = new Super2d3dGraphLibrary.SeriesFactory();
                //sf.AddValue(1);
                //sf.AddValue(2);
                //sf.AddValue(3);
                foreach (DictionaryEntry de in m_lpgData)
                {
                    bool bFirst = true;
                    foreach (TOscRecord rec in (ArrayList)de.Value)
                    {
                        double val = rec.m_lpgTime - lpgOffset * 100;
                        //val = val * ((rec.m_difPress + rec.m_manPress) / m_refPress);
                        val = val * Math.Sqrt((m_refTemp + 2731.5) / (rec.m_tLpg + 2731.5));
                        val = val + lpgOffset * 100;
                        double[] x = new double[3];
                        //ArrayList x = new ArrayList();
                        x[0]=(rec.m_manPress);
                        x[1]=(val);
                        x[2]=(rec.m_manPress + rec.m_difPress);
//                        ArrayList ar = new ArrayList(3);
  //                      ar.Add(rec.m_manPress);
    //                    ar.Add(val);
      //                  ar.Add(rec.m_manPress + rec.m_difPress);

        //                sf.AddBubble((float)rec.m_manPress, (float)val, (float)(rec.m_manPress + rec.m_difPress));
                        //sf.AddValue(x);
                        //ar[1].Add(x);
                        //ar[0].Add(val);
                        //ar[0].Add(rec.m_manPress + rec.m_difPress);
                        //surface2.Add(rec.m_manPress, val, rec.m_manPress + rec.m_difPress);

                    }
                }
                super2d3dGraph1.Series = ar;
                //sf.ApplyTo(super2d3dGraph1);*/

            }
            if (cleanPetfactor != 0 && cleanLpgfactor != 0)
                textBox3.Text = (cleanLpgfactor /cleanPetfactor).ToString();

            chart2.ChartAreas["AproxArea"].CursorX.IsUserEnabled = true;
            chart2.ChartAreas["AproxArea"].CursorX.IsUserSelectionEnabled = true;
            chart2.ChartAreas["AproxArea"].CursorY.IsUserEnabled = true;
            chart2.ChartAreas["AproxArea"].CursorY.IsUserSelectionEnabled = true;

            chart2.ChartAreas["MainArea"].CursorX.IsUserEnabled = true;
            chart2.ChartAreas["MainArea"].CursorX.IsUserSelectionEnabled = true;
            chart2.ChartAreas["MainArea"].CursorY.IsUserEnabled = true;
            chart2.ChartAreas["MainArea"].CursorY.IsUserSelectionEnabled = true;

            chart3.ChartAreas["AproxArea"].CursorX.IsUserEnabled = true;
            chart3.ChartAreas["AproxArea"].CursorX.IsUserSelectionEnabled = true;
            chart3.ChartAreas["AproxArea"].CursorY.IsUserEnabled = true;
            chart3.ChartAreas["AproxArea"].CursorY.IsUserSelectionEnabled = true;

            chart3.ChartAreas["MainArea"].CursorX.IsUserEnabled = true;
            chart3.ChartAreas["MainArea"].CursorX.IsUserSelectionEnabled = true;
            chart3.ChartAreas["MainArea"].CursorY.IsUserEnabled = true;
            chart3.ChartAreas["MainArea"].CursorY.IsUserSelectionEnabled = true;

            chart4.ChartAreas["MainArea"].CursorX.IsUserEnabled = true;
            chart4.ChartAreas["MainArea"].CursorX.IsUserSelectionEnabled = true;
            chart4.ChartAreas["MainArea"].CursorY.IsUserEnabled = true;
            chart4.ChartAreas["MainArea"].CursorY.IsUserSelectionEnabled = true;

        }

        private void calcAndDrawPet(ref double factor, ref double offset, bool redraw)
        {
            if (m_petData.Count != 0)
            {
                if (factor == 0 && offset == 0)
                    doCalcPet(ref offset, ref factor, redraw);
                // Calc again with clarified data
                doCalcPet(ref offset, ref factor, redraw);
                drawPetPage(factor, offset);
                numericUpDown14.Value = System.Convert.ToDecimal(offset);
                numericUpDown15.Value = System.Convert.ToDecimal(factor);
            }

        }

        private void drawPetPage(double factor, double offset)
        {
            chart2.Series["petOffset"].Points.Clear();
            for (int i = 0; i < m_maxMap; i++)
            {
                chart2.Series["petOffset"].Points.AddXY(i, offset);
            }
            chart2.Series["pet"].Points.Clear();
            for (int i = 0; i < m_maxMap; i++)
            {
                double t = (factor * i) + offset * 100;
                chart2.Series["pet"].Points.AddXY(i, t);
            }
        }
        
        private void calcAndDrawLPG(ref double lpgFactor, ref double lpgOffset, bool redraw)
        {
            if (m_lpgData.Count != 0)
            {
                if (lpgFactor == 0 && lpgOffset==0)
                    doCalcLpg(ref lpgOffset, ref lpgFactor, redraw);
                // Calc again with clarified data
                doCalcLpg(ref lpgOffset, ref lpgFactor, redraw);
                drawLPGPage(lpgFactor, lpgOffset);
                numericUpDown13.Value = System.Convert.ToDecimal(lpgFactor);
                numericUpDown12.Value = System.Convert.ToDecimal(lpgOffset);
                doCalcOffsets(m_lpgData);
            }
        }

        private void drawLPGPage(double lpgFactor, double lpgOffset)
        {
            chart3.Series["lpgOffset"].Points.Clear();
            chart3.Series["lpg"].Points.Clear();
            chart4.Series["lpg"].Points.Clear();
            for (int i = 0; i < m_maxMap; i++)
            {
                chart3.Series["lpgOffset"].Points.AddXY(i, lpgOffset);
                double t = (lpgFactor * i) + lpgOffset * 100;
                chart3.Series["lpg"].Points.AddXY(i, t);
                t = (lpgFactor * ((double)(m_refPress) / (i + m_refDiffPress)) * i) + lpgOffset * 100;
                chart4.Series["lpg"].Points.AddXY(i, t);

            }
            chart4.Series["lpgTime"].Points.Clear();
            chart4.Series["lpgTimePlain"].Points.Clear();
            foreach (DictionaryEntry de in m_lpgData)
            {
                foreach (TOscRecord rec in (ArrayList)de.Value)
                {
                    double val = rec.m_lpgTime - lpgOffset * 100;
                    val = val * Math.Sqrt((m_refTemp + 2731.5) / (rec.m_tLpg + 2731.5));
                    chart4.Series["lpgTimePlain"].Points.AddXY(de.Key, val + lpgOffset * 100);
                    val = val * ((rec.m_difPress + rec.m_manPress) / (m_refDiffPress + rec.m_manPress));
                    chart4.Series["lpgTime"].Points.AddXY(de.Key, val + lpgOffset * 100);
                }
            }
        }
        private void doCalcPet(ref double petOffset, ref double cleanPetfactor, bool redraw)
        {
            petOffset = factorCalculateByOffset(m_petData, chart2.Series["petTime"].Points, chart2.Series["petAprox"].Points, chart2.Series["petFactorAprox"].Points, chart2.Series["petOffsetAprox"].Points, false, petOffset, cleanPetfactor, redraw);
            foreach (DataPoint dp1 in chart2.Series["petFactorAprox"].Points)
            {
                if (dp1.IsEmpty)
                    continue;
                cleanPetfactor = dp1.YValues[0];
                break;
            }
        }

        private void doCalcLpg(ref double lpgOffset, ref double cleanLpgfactor, bool redraw)
        {
            lpgOffset = factorCalculateByOffset(m_lpgData, chart3.Series["lpgTime"].Points, chart3.Series["lpgAprox"].Points, chart3.Series["lpgFactorAprox"].Points, chart3.Series["lpgOffsetAprox"].Points, true, lpgOffset, cleanLpgfactor, redraw);
            foreach (DataPoint dp1 in chart3.Series["lpgFactorAprox"].Points)
            {
                if (dp1.IsEmpty)
                    continue;
                cleanLpgfactor = dp1.YValues[0];
                break;
            }
        }

        private double calcPetOffset(double factor, DataPointCollection src, DataPointCollection dst)
        {
            dst.Clear();
            foreach (DataPoint dp in src)
            {
                if (dp.IsEmpty)
                    continue;
                double petOffset = (dp.YValues[0] - factor * (dp.XValue)) / 100;
                dst.AddXY(dp.XValue, petOffset);
            }
            /*Hashtable ht = new Hashtable();
            fillHashTable(ht, dst);
            dst.Clear();
            calcGoodAvgHashTable(ht, dst, m_calcMaxTimeDiff, false, 0);*/
            return calcGoodAvg(dst, m_calcMaxTimeDiff, false);
        }

        private double calcGoodAvg(DataPointCollection col, double takeOutPerc, bool bDirect)
        {
            double avg = 0;
            bool bFirst = true;
            foreach (DataPoint dp  in col)
            {
                if (dp.IsEmpty)
                    continue;
                if (bFirst)
                {
                    avg = dp.YValues[0];
                    bFirst = false;
                    continue;
                }
                avg = (avg + dp.YValues[0]) / 2;
            }
            double perc = Math.Abs(avg * takeOutPerc);
            if (bDirect)
                perc = takeOutPerc;
            double newAvg = 0;
            bFirst = true;
            foreach (DataPoint dp in col)
            {
                if (dp.IsEmpty)
                    continue;
                double diff = Math.Abs(Math.Abs(avg) - Math.Abs(dp.YValues[0]));
                if (Math.Abs(Math.Abs(avg) - Math.Abs(dp.YValues[0])) > perc)
                    continue; // Value is to far from average
                if (bFirst)
                {
                    newAvg = dp.YValues[0];
                    bFirst = false;
                    continue;
                }
                newAvg = (newAvg + dp.YValues[0]) / 2;
            }
            return newAvg;
        }

        private void chart1_SelectionRangeChanged(object sender, CursorEventArgs e)
        {
            if(checkBox1.Checked == true)
                fillSecondChart(m_selStart, m_selEnd);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            m_petData=new Hashtable();
            chart2.Series["petTime"].Points.Clear();
            chart2.Series["petAprox"].Points.Clear();
            chart2.Series["petFactorAprox"].Points.Clear();
            chart2.Series["pet"].Points.Clear();
            chart2.Series["petOffsetAprox"].Points.Clear();
            chart2.Series["petOffset"].Points.Clear();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            m_lpgData = new Hashtable();
            chart3.Series["lpgTime"].Points.Clear();
            chart3.Series["lpgAprox"].Points.Clear();
            chart3.Series["lpgFactorAprox"].Points.Clear();
            chart3.Series["lpg"].Points.Clear();
            chart3.Series["lpgOffsetAprox"].Points.Clear();
            chart3.Series["lpgOffset"].Points.Clear();
            chart4.Series["lpgTime"].Points.Clear();
            chart4.Series["lpgTimePlain"].Points.Clear();
            chart4.Series["lpg"].Points.Clear();

        }

        private void processSelection(EventArgs e, Chart chart, ref DataPoint curSel)
        {
            if (typeof(MouseEventArgs) != e.GetType())
                return;
            HitTestResult hr = chart.HitTest(((MouseEventArgs)e).X, ((MouseEventArgs)e).Y);
            if (hr.ChartElementType != ChartElementType.DataPoint)
            {
                return;
            }
            if (curSel != null)
            {
                //if (curSel == hr.ChartArea)
                curSel.IsValueShownAsLabel = false;
            }
            //curSel = hr.ChartArea;
            curSel = (DataPoint)hr.Object;
            curSel.IsValueShownAsLabel = true;
        }

        private void chart2_Click(object sender, EventArgs e)
        {
            processSelection(e, chart2, ref c2CurSelect);
        }

        private double factorCalcByOffset(double offset, double fac, Hashtable data, DataPointCollection src, DataPointCollection factor, DataPointCollection factorAprox, bool forLpg, bool redraw)
        {
            src.Clear();
            if(forLpg)
            {
                processHashTableForLpg(data, src, m_calcMaxTimeDiff, false, m_skipTimePoints, offset, fac);
            }else
            {
                processHashTableForPet(data, src, m_calcMaxTimeDiff, false, m_skipTimePoints, offset, fac);
            }
            factor.Clear();
            foreach (DataPoint dp in src)
            {
                if (dp.IsEmpty)
                    continue;
                double t = dp.YValues[0] - offset*100;
                double p = dp.XValue ;
                p = t / p;
                if (p < 8 || p > 50)
                    continue; // Wrong result
                factor.AddXY(dp.XValue, p);
            }
         /*   Hashtable ht = new Hashtable();
            fillHashTable(ht, factor);
            factor.Clear();
            calcGoodAvgHashTable(ht, factor, 0.5f, true, 1);*/

            double cleanPetfactor = fac;
            if(!redraw)
                cleanPetfactor = calcGoodAvg(factor, 0.5f, true);
            factorAprox.Clear();
            for (int i = 0; i < m_maxMap; i++)
            {
                factorAprox.AddXY(i, cleanPetfactor);
            }
            return cleanPetfactor;
        }
        private double factorCalculateByOffset(Hashtable data, DataPointCollection src, DataPointCollection factor, DataPointCollection factorAprox, DataPointCollection offsetAprox, bool forLpg, double offset, double fac, bool redraw)
        {
            double bestOffset = offset;
            if(offset == 0 && fac == 0 && !redraw)
                bestOffset = factorFindBestOffset(data, src, factor, factorAprox, offsetAprox, forLpg);
            double fc = factorCalcByOffset(bestOffset, fac, data, src, factor, factorAprox, forLpg, redraw);
            double offset1 = bestOffset;
            /*if(!redraw)
                offset1 = calcPetOffset(fc, src, offsetAprox);*/
            return offset1;
        }
        private double factorFindBestOffset(Hashtable data, DataPointCollection src, DataPointCollection factor, DataPointCollection factorAprox, DataPointCollection offsetAprox, bool forLpg)
        {
            double offset = factorFindBestRangeOffset(data, m_offsetMin, m_offsetMax, m_offsetRoughStep, src, factor, factorAprox, offsetAprox, forLpg);
            double range = m_offsetExactRange / 2;
            offset = factorFindBestRangeOffset(data, offset - range, offset + range, m_offsetExactStep, src, factor, factorAprox, offsetAprox, forLpg);
            return offset;
        }

        private double factorFindBestRangeOffset(Hashtable data, double from, double to, double step, DataPointCollection src, DataPointCollection factor, DataPointCollection factorAprox, DataPointCollection offsetAprox, bool forLpg)
        {
            double range = 0;
            double rangeDiff = 0;
            double bestOffset = from;;
            for (double offset = from; offset <= to; offset += step)
            {
                double avg = factorCalcByOffset(offset, 0, data, src, factor, factorAprox, forLpg, false);
                //double min = 0, max = 0;
                //int less = 0, more = 0;
                double diff = 0;
                double offset1 = calcPetOffset(avg, src, offsetAprox);
                diff = Math.Abs(Math.Abs(offset1) - Math.Abs(offset));
                double totalDiff = 0;
                foreach (DataPoint dp in src)
                {
                    double t = avg * (dp.XValue) + offset * 100;
                    totalDiff = totalDiff + Math.Abs(t - dp.YValues[0]);
                }
/*                foreach (DataPoint dp in offsetAprox)
                {
                    totalDiff = totalDiff + Math.Abs(offset1 - dp.YValues[0]);
                }*/
/*                foreach (DataPoint dp1 in factor)
                {
                    if (dp1.IsEmpty)
                        continue;
                    if (min == 0 && max == 0 && diff == 0)
                    {
                        min = dp1.YValues[0];
                        max = dp1.YValues[0];
                        diff = Math.Abs(Math.Abs(avg) - Math.Abs(dp1.YValues[0]));
                        continue;
                    }
                    if (dp1.YValues[0] < min)
                        min = dp1.YValues[0];
                    if (dp1.YValues[0] > max)
                        max = dp1.YValues[0];
                    if (dp1.YValues[0] > avg)
                        more++;
                    if (dp1.YValues[0] < avg)
                        less++;
                    diff = (diff + Math.Abs(Math.Abs(avg) - Math.Abs(dp1.YValues[0]))) / 2;
                }*/
                //if (bestOffset == from || (max - min + more - less + diff) < range)
                double d = range * m_calcMaxTimeDiff;
                if (bestOffset == from || (totalDiff) < range/* || (Math.Abs(totalDiff - range) < d && diff < rangeDiff)*/)
                {
                    bestOffset = offset;
                    //range = max - min + more - less + diff;
                    range = totalDiff;
                    rangeDiff = diff;
                }
            }
            return bestOffset;

        }

        private void chart3_Click(object sender, EventArgs e)
        {
            processSelection(e, chart3, ref c3CurSelect);
        }

        private void chart1_SelectionRangeChanging(object sender, CursorEventArgs e)
        {
            if (e.Axis.AxisName!= AxisName.X)
                return;
            m_selEnd = e.NewSelectionEnd;
            m_selStart = e.NewSelectionStart;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            fillSecondChart(0, m_records);
        }

        private void chart1_Click(object sender, EventArgs e)
        {
            processSelection(e, chart1, ref c1CurSelect);
            doCalcOffsets();
        }

        private void tChart1_Click(object sender, EventArgs e)
        {

        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void checkBox3_CheckedChanged(object sender, EventArgs e)
        {
            chart4.Series["lpgTimePlain"].Enabled = checkBox3.Checked;
        }

        private void chart4_Click(object sender, EventArgs e)
        {
            processSelection(e, chart4, ref c4CurSelect);
        }

        private void button4_Click(object sender, EventArgs e)
        {
            double factor = System.Convert.ToDouble(numericUpDown13.Value);
            double offset = System.Convert.ToDouble(numericUpDown12.Value);
            calcAndDrawLPG(ref factor, ref offset, true);
        }

        private void button5_Click(object sender, EventArgs e)
        {
            double factor = System.Convert.ToDouble(numericUpDown15.Value);
            double offset = System.Convert.ToDouble(numericUpDown14.Value);
            calcAndDrawPet(ref factor, ref offset, true);
        }

        private void button6_Click(object sender, EventArgs e)
        {
            double lpgOffset = System.Convert.ToDouble(numericUpDown25.Value);
            double idleManP = System.Convert.ToDouble(numericUpDown21.Value);
            double minInjT = System.Convert.ToDouble(numericUpDown17.Value);
            double maxInjCycleT = System.Convert.ToDouble(numericUpDown18.Value);
            double lpgOpenT = System.Convert.ToDouble(numericUpDown26.Value);
            double maxManP = System.Convert.ToDouble(numericUpDown27.Value);
            double suggDiffP = 150; // instead of any
            double minDiffP = 90;
            double tLpg = System.Convert.ToDouble(numericUpDown24.Value);
            if (!LpgSysCalcs.calcSuggPressRange(ref minDiffP, ref suggDiffP, lpgOffset, lpgOpenT, idleManP, maxManP, minInjT, maxInjCycleT, tLpg, 40, System.Convert.ToDouble(numericUpDown24.Maximum)))
            {
                MessageBox.Show("Injectors don't suit for this injection model.");
                return;
            }
            textBox1.Text = minDiffP.ToString("0") + " - " + suggDiffP.ToString("0");
            //numericUpDown22.Minimum = System.Convert.ToDecimal(minDiffP);
            numericUpDown22.Maximum = System.Convert.ToDecimal(suggDiffP);
            //numericUpDown22.Value = System.Convert.ToDecimal((suggDiffP + minDiffP)/2);

            /*double currD = System.Convert.ToDouble(numericUpDown20.Value);
            double currDiffP = System.Convert.ToDouble(numericUpDown22.Value);
            double currLpgIdleT = System.Convert.ToDouble(numericUpDown23.Value);
            double newDiffP = System.Convert.ToDouble(numericUpDown24.Value);
            currLpgIdleT = currLpgIdleT - lpgOffset;
            double factor = (currLpgIdleT / minInjT) * ((currDiffP + idleManP) / (newDiffP + idleManP));
            currD = currD * Math.Sqrt(factor);
            numericUpDown19.Value = System.Convert.ToDecimal(currD);*/

            double currDiffP = System.Convert.ToDouble(numericUpDown22.Value);

            double engVol = System.Convert.ToDouble(numericUpDown23.Value);
            short engPistons = System.Convert.ToInt16(numericUpDown20.Value);
            double tAir = System.Convert.ToDouble(numericUpDown29.Value);
            if (tAir > 60)
                tAir = 60;
            double vAir = LpgSysCalcs.calcAirVol(LpgSysCalcs.calcPistonDispacement(engVol, engPistons));
            double mAir = LpgSysCalcs.calcAirMass(idleManP, vAir, tAir);
            double mLpg = mAir / LpgSysCalcs.c_LpgLambda;
            double mFlowLpg = mLpg * 1000 / (minInjT - lpgOffset);
            //double A = Math.Sqrt(2 * (adb / (adb + 1)) * (Math.Pow(2 / (adb + 1), 2 / (adb - 1)) - Math.Pow(2 / (adb + 1), adb / (adb - 1))));
            //double S = (mFlowLpg * Math.Sqrt(8.31f * (tLpg + 273.15f)) / (A*(currDiffP + idleManP) * 1000));

            //double A = Math.Sqrt(2*adb/(adb+1) * Math.Pow(2 / (adb + 1), (adb + 1) /(adb - 1)));
            double S = LpgSysCalcs.calcAreafromLpgMassFlow(mFlowLpg, currDiffP + idleManP, tLpg);
            if (radioButton2.Checked)
                S = S / 2;
            double d = LpgSysCalcs.calcCircleDiameter(S) * 1000;
            double rD = System.Convert.ToDouble(numericUpDown19.Value);
            numericUpDown19.Value = System.Convert.ToDecimal(d);
            numericUpDown19.ReadOnly = true;
            double SitD = 4;
            double pGap = S / (Math.PI * SitD / 1000) * 1.20f * 1000;
            if (pGap < 0.3f)
                pGap = 0.3f;
            pGap = Math.Round(Math.Round(pGap * 20) / 20, 2, MidpointRounding.AwayFromZero);
            textBox4.Text=pGap.ToString();
            /*d=2.2f / 1000;
            double mFlow1 = (Math.PI * Math.Pow(d, 2)) / 4 * A * (currDiffP + idleManP) * 1000 / Math.Sqrt(8.31f * (tLpg + 273.15f));
            double mLpg1 = mFlow1 * minInjT / 1000;
            double mAir1=mLpg1 *15.5f;
            double mPet = 0.002977 * 3.3f/1000;
            double mAir2 = mPet * 14.7f;*/

            rD = rD / 1000;
            double rS = LpgSysCalcs.calcCircleArea(rD);
            double rFlow = LpgSysCalcs.calcLpgMassFlowfromArea(rS, currDiffP + idleManP, tLpg);
            if (radioButton2.Checked)
                rFlow = rFlow * 2;
            double rTime = mLpg * 1000 / rFlow;
            textBox2.Text = rTime.ToString();

        }

        private void numericUpDown26_ValueChanged(object sender, EventArgs e)
        {

        }

        private void doCalcOffsets(Hashtable ht)
        {
            double rD = System.Convert.ToDouble(numericUpDown19.Value) / 1000;
            double rS = LpgSysCalcs.calcCircleArea(rD);

            bool bFirst = true;
            double avg = 0;
            foreach (DictionaryEntry de in ht)
            {
                foreach (TOscRecord rec in (ArrayList)de.Value)
                {
                    double val = doCalcLpgOffset(rec);
                    if (bFirst)
                    {
                        avg = val;
                        bFirst = false;
                        continue;
                    }
                    avg = (avg + val) / 2;
                }
            }

            textBox2.Text = avg.ToString();

        }

        private void doCalcOffsets()
        {
            if (c1CurSelect == null)
                return;
            double rD = System.Convert.ToDouble(numericUpDown19.Value) / 1000;
            double rS = LpgSysCalcs.calcCircleArea(rD);

            TOscRecord oscRec = (TOscRecord)m_oscData[c1CurSelect.XValue];
            double rTime = doCalcLpgOffset(oscRec);
            textBox2.Text = rTime.ToString();

        }

        private double doCalcLpgOffset(TOscRecord oscRec)
        {
            double rD = System.Convert.ToDouble(numericUpDown19.Value) / 1000;
            double engVol = System.Convert.ToDouble(numericUpDown23.Value);
            short engPistons = System.Convert.ToInt16(numericUpDown20.Value);
            return LpgSysCalcs.calcLpgOffset(rD, engVol, engPistons, (oscRec.m_tLpg / 10) + 10, oscRec.m_tLpg / 10, oscRec.m_manPress, oscRec.m_difPress + oscRec.m_manPress, oscRec.m_lpgTime / 100, radioButton2.Checked);
            //double tAir = oscRec.m_tLpg/10;
/*            double tAir = (oscRec.m_tLpg / 10) + 10;
            double tLpg = oscRec.m_tLpg / 10;
            double vAir = LpgSysCalcs.calcPistonDispacement(engVol, engPistons);
            double mAir = LpgSysCalcs.calcAirMass(oscRec.m_manPress, vAir, tAir);
            double mLpg = mAir / LpgSysCalcs.c_LpgLambda;
            double rFlow = LpgSysCalcs.calcLpgMassFlowfromArea(rS, oscRec.m_difPress + oscRec.m_manPress, tLpg);
            if (radioButton2.Checked)
                rFlow = rFlow / 2;
            double rTime = mLpg * 1000 / rFlow;
            rTime = oscRec.m_lpgTime / 100 - rTime;
            return rTime;*/
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch((sender as ComboBox).SelectedIndex)
            {
                case 2:
                    {
                        numericUpDown25.Enabled = false;
                        numericUpDown26.Enabled = false;
                        numericUpDown25.Value = System.Convert.ToDecimal(0.2f);
                        numericUpDown26.Value = System.Convert.ToDecimal(3.3f);
                        break;
                    }
                case 1:
                    {
                        numericUpDown25.Enabled = false;
                        numericUpDown26.Enabled = false;
                        numericUpDown25.Value = System.Convert.ToDecimal(-1.7f);
                        numericUpDown26.Value = System.Convert.ToDecimal(2.4f);
                        break;
                    }
                case 3:
                    {
                        numericUpDown25.Enabled = false;
                        numericUpDown26.Enabled = false;
                        numericUpDown25.Value = System.Convert.ToDecimal(-2.25f);
                        numericUpDown26.Value = System.Convert.ToDecimal(2.2f);
                        break;
                    }
                case 4:
                    {
                        numericUpDown25.Enabled = false;
                        numericUpDown26.Enabled = false;
                        numericUpDown25.Value = System.Convert.ToDecimal(-1.3f);
                        numericUpDown26.Value = System.Convert.ToDecimal(3.2f);
                        break;
                    }
                case 5:
                    {
                        numericUpDown25.Enabled = false;
                        numericUpDown26.Enabled = false;
                        numericUpDown25.Value = System.Convert.ToDecimal(0.2f);
                        numericUpDown26.Value = System.Convert.ToDecimal(2.4f);
                        break;
                    }
                default:
                    {
                        numericUpDown25.Enabled = true;
                        numericUpDown26.Enabled = true;
                        break;
                    }
            }
        }

        private void button7_Click(object sender, EventArgs e)
        {
            double rD = System.Convert.ToDouble(numericUpDown19.Value) / 1000;
            double idleManP = System.Convert.ToDouble(numericUpDown21.Value);
            double minInjT = System.Convert.ToDouble(numericUpDown17.Value);
            double currDiffP = System.Convert.ToDouble(numericUpDown22.Value);
            double engVol = System.Convert.ToDouble(numericUpDown23.Value);
            short engPistons = System.Convert.ToInt16(numericUpDown20.Value);
            double tLpg = System.Convert.ToDouble(numericUpDown24.Value);
            double tAir = System.Convert.ToDouble(numericUpDown29.Value);
            if (tAir > 60)
                tAir = 60;

            double lpgOffset = LpgSysCalcs.calcLpgOffset(rD, engVol, engPistons, tAir, tLpg, idleManP, currDiffP + idleManP, minInjT, radioButton2.Checked);
            numericUpDown25.Value=System.Convert.ToDecimal(lpgOffset);
        }

        private void numericUpDown28_DoubleClick(object sender, EventArgs e)
        {
            numericUpDown28.Value = System.Convert.ToDecimal(LpgSysCalcs.calcTamonaPress(1.315f, System.Convert.ToInt16(numericUpDown28.Value)));

        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AboutBox ab=new AboutBox();
            ab.ShowDialog();
        }

        private void button8_Click(object sender, EventArgs e)
        {
            double rD = System.Convert.ToDouble(numericUpDown32.Value) / 1000;
            double idleManP = System.Convert.ToDouble(numericUpDown43.Value);
            double idleManPLpg = System.Convert.ToDouble(numericUpDown36.Value);
            double petInjT = System.Convert.ToDouble(numericUpDown44.Value);
            double lpgInjT = System.Convert.ToDouble(numericUpDown45.Value);
            double currDiffP = System.Convert.ToDouble(numericUpDown31.Value);
            double engVol = System.Convert.ToDouble(numericUpDown33.Value);
            short engPistons = System.Convert.ToInt16(numericUpDown30.Value);
            double tLpg = System.Convert.ToDouble(numericUpDown35.Value);
            double tAir = System.Convert.ToDouble(numericUpDown34.Value);
            bool fullGroup=radioButton3.Checked;
            bool toSemiFull = checkBox7.Checked;

            if (!fullGroup)
                toSemiFull = false;
            if (toSemiFull)
                fullGroup = false;

            double petOffset = System.Convert.ToDouble(numericUpDown42.Value);
            
            if (tAir > 60)
                tAir = 60;
            double refPress=currDiffP + idleManPLpg;

            double lpgOffset = LpgSysCalcs.calcLpgOffset(rD, engVol, engPistons, tAir, tLpg, idleManPLpg, refPress, lpgInjT, fullGroup);
            double lpgCycleT = LpgSysCalcs.adjustCycleTime(idleManPLpg, idleManP, lpgInjT - lpgOffset);
            lpgOffset = lpgInjT - lpgCycleT;
            double petCycleT = petInjT - petOffset;
            if (toSemiFull)
                petCycleT *= 2;
            double factor = lpgCycleT / petCycleT;
            double calcPress=refPress;
            if (checkBox6.Checked)
            {
                calcPress = System.Convert.ToDouble(numericUpDown47.Value);
            }
            factor = LpgSysCalcs.adjustFactor(factor, tLpg, refPress, LpgSysCalcs.c_TamonaRefTemp, calcPress);
            numericUpDown41.Value = System.Convert.ToDecimal(lpgOffset);
            numericUpDown46.Value = System.Convert.ToDecimal(factor);
            numericUpDown47.Value = System.Convert.ToDecimal(calcPress);

        }

    }
}