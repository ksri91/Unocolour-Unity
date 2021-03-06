﻿using UnityEngine;
using System;
using System.Collections.Generic;
using System.Collections;

public class BoardManager : MonoBehaviour
{

    public GameObject cardPrefab;
    public GameObject stackPrefab;
    public List<CardColor> deckColors;
    public List<int> deckColorNumbers;
    public GameObject cameraObject;
    public GameObject scoresheet;
    new Camera camera;

    readonly static float DistributeTime = 0.05f;
    readonly static int columns = 10;
    readonly static int rows = 5;
    readonly static CellPos lastCell = new CellPos(columns, rows);
    readonly static float halfcardwidth = 0.3f;
    readonly static float halfcardheight = 0.4f;
    readonly static Vector3 cellwidth = new Vector3(0.83f, 0f, 0f);
    readonly static Vector3 cellheight = new Vector3(0f, 1f, 0f);
    readonly static Vector3 cellsize = cellheight + cellwidth;
    readonly static Vector3 firstcellpos = new Vector3(0f, 1f, 0f) - (columns - 1) / 2f * cellwidth - (rows - 1) / 2f * cellheight;

    //CardStack[,] cells = new CardStack[columns, rows];
    Dictionary<CellPos, CardStack> cells = new Dictionary<CellPos, CardStack>(lastCell.area);
    CardStack deck;
    CardStack[] holdingStacks = new  CardStack[4];
    CellPos[] moveCellPos = new CellPos[4];
    int score;
    bool animating;
    bool stateTransition;
    int round;
    int cardsplayed;
    enum State { start, roundstart, shuffle, shuffleAnim, deal, dealAnim, playStart, play, collect, collectAnim, scoreboard, finalscoreboard };
    CellPos mousePos;

    public class StateMachine<StateType>
    {
        public Dictionary<StateType, Func<StateType>> ProcessMap;
        StateType state;

        public StateMachine(StateType InitialState)
        {
            state = InitialState;
        }

        public void Process()
        {
            state = ProcessMap[state]();
        }
    }

    StateMachine<State> state = new StateMachine<State>(State.start);

    public BoardManager()
    {
        state.ProcessMap = new Dictionary<State, Func<State>> { { State.start,GameStart }, { State.roundstart,RoundStart },
            { State.shuffle,Shuffle }, { State.shuffleAnim,ShuffleAnim }, { State.deal,Deal },
            { State.dealAnim,DealAnim }, { State.playStart,PlayStart }, { State.play,Play }, { State.collect,Collect },
            { State.collectAnim, CollectAnim }, { State.scoreboard, Scoreboard }, { State.finalscoreboard, FinalScoreboard }
        };
    }


    State GameStart()
    {
        round = 0;
        score = 0;
        animating = false;
        return State.roundstart;
    }
    State RoundStart()
    {
        round++;
        return State.shuffle;
    }
    State Shuffle()
    {
        deck.Shuffle();
        return State.shuffleAnim;
    }
    State ShuffleAnim()
    {
        if (animating)
            return State.shuffleAnim;
        if (round == 1)
            return State.deal;
        return State.playStart;
    }
    State Deal()
    {
        StartCoroutine(DistributeToActiveCells());
        return State.dealAnim;
    }
    State DealAnim()
    {
        if (animating)
            return State.dealAnim;
        return State.playStart;
    }
    State PlayStart()
    {
        cardsplayed = 0;
        board.RecalculateActiveShapes(cellsAsCardColor);
        SyncCardActives();
        if (deck.number < 4 || !board.IsAnyMovePossible())
        {
            return State.collect;
        }
        return State.play;
    }
    State Play()
    {
        if (Input.GetMouseButtonDown(0) && mousePos != null)
        {
            if (board.CellActive(mousePos,true))
            {
                holdingStacks[cardsplayed].ReceiveCard(deck.SendCard());
                moveCellPos[cardsplayed] = mousePos;
                cardsplayed += 1;
                cells[mousePos].Click();
                //cells[mousePos].ReceiveCard(deck.SendCard());
                board.AddCellToMove(mousePos);
                SyncCardActives();
            }
            if (cardsplayed == 4)
            {
                for(int i = 0; i < 4; i++)
                {
                    cells[moveCellPos[i]].ReceiveCard(holdingStacks[i].SendCard());
                }
                return State.playStart;
            }
        }
        return State.play;
    }
    State Collect()
    {
        StartCoroutine(CollectFromCells());
        return State.collectAnim;
    }
    State CollectAnim()
    {
        if (animating)
            return State.collectAnim;
        return State.scoreboard;
    }
    State Scoreboard()
    {
        foreach (CellPos pos in lastCell.Range())
        {
            score += round * Triangle(cells[pos].number);
        }

        scoresheet.GetComponent<Scoresheet>().ShowScore(score, deck.number == 108);

        return State.finalscoreboard;
    }
    State FinalScoreboard()
    {
        if (scoresheet.activeSelf)
        {
            return State.finalscoreboard;
        }
        if (deck.number == 108)
        {
            SceneChanger.instance.LoadMainMenu();
        }
        return State.roundstart;
    }



    BoardEngine board;


    void InstantiateBoard()
    {
        // Making the deck
        deck = MakeStack(new Vector3(0f, -3f, 0f) - 3 * cellwidth, true, false, true);

        // Making the individual stacks.
        foreach (CellPos i in lastCell.Range())
        {
            cells[i] = MakeStack(firstcellpos + i * cellsize, true, true, true);
        }

        // Initializing the holding cells.
        for (int i = 0; i < 4; i++)
        {
            holdingStacks[i] = MakeStack(new Vector3(0f, -3f, 0f) + i * cellwidth, true, true, false);
        }

        // Making the cards
        for (int i = 0; i < deckColors.Count; i++)
        {
            for (int j = 0; j < deckColorNumbers[i]; j++)
            {
                MakeCard(deckColors[i], deck);
            }
        }


        board = new BoardEngine(new CellPos(columns, rows));

    }

    CardStack MakeStack(Vector3 position, bool active, bool staggered, bool numbered)
    {
        GameObject stackObject = Instantiate(stackPrefab, position, Quaternion.identity, transform) as GameObject;
        CardStack stack = stackObject.GetComponent<CardStack>();
        stack.SetProperties(active, staggered, numbered);
        return stack;
    }

    void MakeCard(CardColor color, CardStack stack)
    {
        GameObject cardObject = Instantiate(cardPrefab, stack.transform.position, Quaternion.identity, transform) as GameObject;
        cardObject.GetComponent<Card>().color = color;
        stack.ReceiveCard(cardObject);
    }

    IEnumerator DistributeToActiveCells()
    {
        animating = true;
        foreach (CellPos i in lastCell.Range(true, false, true))
        {
            cells[i].ReceiveCard(deck.SendCard());
            yield return new WaitForSeconds(DistributeTime);
        }
        animating = false;
    }

    IEnumerator CollectFromCells()
    {
        animating = true;
        for (int i = 0; i < round; i++)
        {
            foreach (CellPos pos in lastCell.Range(true, false, false))
            {
                if (cells[pos].number > 0)
                {
                    deck.ReceiveCard(cells[pos].SendCard());
                    yield return new WaitForSeconds(DistributeTime);
                }
            }
        }
        animating = false;
    }


    void SyncCardActives()
    {
        foreach (CellPos i in lastCell.Range())
        {
            cells[i].active = board.CellActive(i,false);
        }
    }
    Dictionary<CellPos, CardColor> cellsAsCardColor
    {
        get
        {
            Dictionary<CellPos, CardColor> result = new Dictionary<CellPos, CardColor>(lastCell.area);
            foreach (CellPos i in lastCell.Range())
            {
                result[i] = cells[i].topCardColor;
            }
            return result;
        }
    }

    int Triangle(int n)
    {
        return n * (n + 1) / 2;
    }


    CellPos GetMouseCell()
    {
        Vector3 currentPos = camera.ScreenToWorldPoint(Input.mousePosition);
        //Debug.Log (currentPos);
        int i, j;
        for (i = 0; i < columns; i++)
        {
            if (Mathf.Abs((currentPos - firstcellpos - i * cellwidth).x) < halfcardwidth)
                break;
        }
        for (j = 0; j < rows; j++)
        {
            //Debug.Log ((currentPos - firstcellpos - j * cellheight).y.ToString ());
            if (Mathf.Abs((currentPos - firstcellpos - j * cellheight).y) < halfcardheight)
                break;
        }

        if (i < columns && j < rows)
        {
            return new CellPos(i, j);
        }
        return null;
    }

    void Start()
    {
        camera = cameraObject.GetComponent<Camera>();
        InstantiateBoard();
        scoresheet.SetActive(false);


    }

    void Update()
    {

        mousePos = GetMouseCell();

        if (mousePos != null && !animating)
        {
            cells[mousePos].Hover();
        }

        state.Process();




    }

}


